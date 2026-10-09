using System.Globalization;
using XFLCSMS.Models.Issue;
using XFLCSMS.Services.Notify;

namespace XFLCSMS.Services
{
    /// <summary>
    /// The rules that act by themselves (System &gt; Automation), read from the settings each time they are asked.
    /// Everything is off until somebody switches it on: an update never starts closing or assigning tickets.
    ///   auto.assign               off | round | least   who gets a new ticket that has no engineer of its product
    ///   auto.close.days           close a ticket this many days after it became "Deployed" (0 = never)
    ///   auto.remind.days          remind the person who raised a ticket every so many days while it is "Pending" (0 = never)
    ///   auto.remind.max           how many reminders at most
    ///   auto.pending.close.days   close a ticket that has been "Pending" this long without a word from the house (0 = never)
    /// </summary>
    public sealed class AutomationRules
    {
        public const string AssignKey = "auto.assign";
        public const string CloseDaysKey = "auto.close.days";
        public const string RemindDaysKey = "auto.remind.days";
        public const string RemindMaxKey = "auto.remind.max";
        public const string PendingCloseDaysKey = "auto.pending.close.days";

        public const string Off = "off";
        /// <summary>In turn: the engineer whose last ticket was assigned longest ago.</summary>
        public const string RoundRobin = "round";
        /// <summary>The engineer with the fewest open tickets.</summary>
        public const string LeastLoad = "least";

        private readonly SettingsStore _settings;

        public AutomationRules(SettingsStore settings)
        {
            _settings = settings;
        }

        public string AssignMode
        {
            get
            {
                var mode = _settings.Get(AssignKey, Off);
                return mode == RoundRobin || mode == LeastLoad ? mode : Off;
            }
        }

        public int CloseDays => Math.Clamp(_settings.GetInt(CloseDaysKey, 0), 0, 365);
        public int RemindDays => Math.Clamp(_settings.GetInt(RemindDaysKey, 0), 0, 365);
        public int RemindMax => Math.Clamp(_settings.GetInt(RemindMaxKey, 2), 1, 10);
        public int PendingCloseDays => Math.Clamp(_settings.GetInt(PendingCloseDaysKey, 0), 0, 365);

        /// <summary>Is there anything for the background check to do?</summary>
        public bool AnyTimed => CloseDays > 0 || RemindDays > 0 || PendingCloseDays > 0;
    }

    /// <summary>
    /// Applies the rules that depend on time, every few minutes: closes tickets that have been "Deployed" long enough,
    /// reminds while a ticket is "Pending", closes tickets nobody answered. (Who gets a new ticket is decided when
    /// the ticket is raised: TicketService.AutoAssign.) What it does is written to the history of the ticket as
    /// done by "Automation", and into its conversation where the brokerage house should read why.
    /// </summary>
    public sealed class AutomationWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<AutomationWorker> _logger;
        private readonly TimeSpan _every;
        private readonly int _maxPerRun;

        public AutomationWorker(IServiceScopeFactory scopes, ILogger<AutomationWorker> logger, IConfiguration configuration)
        {
            _scopes = scopes;
            _logger = logger;
            _every = TimeSpan.FromSeconds(Math.Clamp(configuration.GetValue("Automation:CheckSeconds", 300), 5, 86400));
            _maxPerRun = Math.Clamp(configuration.GetValue("Automation:MaxPerCheck", 20), 1, 1000);
        }

        public DateTime? LastRun { get; private set; }
        public string? LastError { get; private set; }
        /// <summary>What the last round that did something did, e.g. "closed 2, reminded 1" (for the system health page).</summary>
        public string? LastResult { get; private set; }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(25), stoppingToken); } catch (OperationCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                await RunOnceAsync(stoppingToken);
                try { await Task.Delay(_every, stoppingToken); } catch (OperationCanceledException) { return; }
            }
        }

        /// <summary>How many tickets one round acts on at most: switching a rule on must not close a backlog of years (and tell everybody) in one go.</summary>
        public int MaxPerRun => _maxPerRun;

        /// <summary>How often the round runs.</summary>
        public TimeSpan Every => _every;

        /// <summary>One round. Returns how many tickets it acted on. Never throws: the next round tries again.</summary>
        public async Task<int> RunOnceAsync(CancellationToken cancel = default)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var rules = scope.ServiceProvider.GetRequiredService<AutomationRules>();
                if (!rules.AnyTimed)
                {
                    LastRun = DateTime.Now;
                    LastError = null;
                    return 0;
                }

                var db = scope.ServiceProvider.GetRequiredService<DataContext>();
                var tickets = scope.ServiceProvider.GetRequiredService<TicketService>();
                var notify = scope.ServiceProvider.GetRequiredService<NotificationService>();
                tickets.ActAsSystem(TicketService.AutomationName);
                var now = DateTime.Now;
                int closed = 0, reminded = 0;

                // Every ticket is read again right before it is touched and saved right after: somebody who reopens
                // or answers a ticket while this round runs must not have that undone by what was read a moment ago.
                async Task<bool> StillAsync(IssueTable issue, string status)
                {
                    await db.Entry(issue).ReloadAsync(cancel);
                    return issue.IStatus == status;
                }

                // ---- "Deployed" long enough: closed, unless the people who raised it wrote after the deployment
                if (rules.CloseDays > 0)
                {
                    var limit = now.AddDays(-rules.CloseDays);
                    var deployed = (await db.Issues.Where(issue => issue.IStatus == TicketStatus.Deployed).ToListAsync(cancel))
                        .Where(issue => Since(issue) <= limit).OrderBy(Since).ToList();
                    foreach (var issue in deployed)
                    {
                        if (closed >= _maxPerRun) { break; }
                        if (!await StillAsync(issue, TicketStatus.Deployed) || Since(issue) > limit || await HouseWroteAfter(db, issue, Since(issue), cancel)) { continue; }

                        if (tickets.SetStatus(issue, TicketStatus.Closed) == null)
                        {
                            tickets.AddSystemMessage(issue, "<p>This ticket was closed automatically: it has been deployed for " + Days(rules.CloseDays)
                                + " and nobody reported further trouble. If the problem is back, reply here or raise a new ticket.</p>", now);
                            await db.SaveChangesAsync(cancel);
                            closed++;
                        }
                    }
                }

                var pending = rules.RemindDays > 0 || rules.PendingCloseDays > 0
                    ? (await db.Issues.Where(issue => issue.IStatus == TicketStatus.Pending).ToListAsync(cancel)).OrderBy(PendingFrom).ToList()
                    : new List<IssueTable>();

                // ---- "Pending" without a word from the people who raised it for too long: closed
                if (rules.PendingCloseDays > 0)
                {
                    var limit = now.AddDays(-rules.PendingCloseDays);
                    foreach (var issue in pending.Where(issue => PendingFrom(issue) <= limit).ToList())
                    {
                        if (closed >= _maxPerRun) { break; }
                        if (!await StillAsync(issue, TicketStatus.Pending) || PendingFrom(issue) > limit || await HouseWroteAfter(db, issue, PendingFrom(issue), cancel)) { continue; }

                        if (tickets.SetStatus(issue, TicketStatus.Closed) == null)
                        {
                            tickets.AddSystemMessage(issue, "<p>This ticket was closed automatically: it has been waiting for an answer for " + Days(rules.PendingCloseDays)
                                + ". If it is still needed, reply here; " + Infrastructure.Ui.SupportTeam + " can reopen it.</p>", now);
                            await db.SaveChangesAsync(cancel);
                            pending.Remove(issue);
                            closed++;
                        }
                    }
                }

                // ---- "Pending": a reminder every so many days, a limited number of times - unless the answer is there already
                if (rules.RemindDays > 0)
                {
                    foreach (var issue in pending.Where(issue => issue.ReminderCount < rules.RemindMax).ToList())
                    {
                        if (reminded >= _maxPerRun) { break; }
                        var from = PendingFrom(issue);
                        var last = issue.LastReminderAt != null && issue.LastReminderAt > from ? issue.LastReminderAt.Value : from;
                        if (last > now.AddDays(-rules.RemindDays)) { continue; }
                        if (!await StillAsync(issue, TicketStatus.Pending)) { continue; }
                        // the people of the house wrote since it went pending (or since the last reminder): now it is AIPG's turn
                        if (await HouseWroteAfter(db, issue, last, cancel)) { continue; }

                        issue.ReminderCount++;
                        issue.LastReminderAt = now;
                        var waiting = Math.Max(1, (int)(now - from).TotalDays);
                        notify.TicketReminder(issue, waiting);
                        tickets.Log(AuditActions.TicketReminder, issue, "Reminded the person who raised it: pending for " + Days(waiting)
                            + " (reminder " + issue.ReminderCount + " of " + rules.RemindMax + ")");
                        await db.SaveChangesAsync(cancel);
                        reminded++;
                    }
                }

                if (closed + reminded > 0)
                {
                    LastResult = DateTime.Now.ToString("d MMM, HH:mm", CultureInfo.InvariantCulture) + ": closed " + closed + ", reminded " + reminded;
                }

                LastRun = DateTime.Now;
                LastError = null;
                return closed + reminded;
            }
            catch (OperationCanceledException)
            {
                return 0;
            }
            catch (Exception exception)
            {
                LastError = exception.GetBaseException().Message;
                _logger.LogWarning(exception, "The automation rules failed; they run again in {Seconds} seconds", (int)_every.TotalSeconds);
                return 0;
            }
        }

        /// <summary>Since when the ticket has its status; for tickets from before version 3.4, the last change or the day it was raised.</summary>
        public static DateTime Since(IssueTable issue)
        {
            return issue.StatusSince ?? issue.UpdatedOn ?? issue.TDate;
        }

        /// <summary>Since when a pending ticket is pending (tickets that went pending before version 3.3 carry no stamp of their own).</summary>
        public static DateTime PendingFrom(IssueTable issue)
        {
            return issue.PendingSince ?? Since(issue);
        }

        /// <summary>
        /// Did the people who raised the ticket write in its conversation after that moment? An entry of somebody of
        /// the brokerage house, or of the person who raised it (AIPG staff raise tickets too); never an internal note.
        /// </summary>
        public static Task<bool> HouseWroteAfter(DataContext db, IssueTable issue, DateTime since, CancellationToken cancel = default)
        {
            return db.TicketMessages.AnyAsync(message => message.IssueId == issue.IssueId && !message.IsInternal && message.At > since
                && (!message.FromStaff || message.UserId == issue.UserId), cancel);
        }

        private static string Days(int days)
        {
            return days == 1 ? "1 day" : days.ToString(CultureInfo.InvariantCulture) + " days";
        }
    }
}
