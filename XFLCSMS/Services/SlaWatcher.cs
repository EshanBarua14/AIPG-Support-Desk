using XFLCSMS.Models.Issue;
using XFLCSMS.Services.Notify;

namespace XFLCSMS.Services
{
    /// <summary>
    /// Looks at the open tickets once a minute and tells people when a service target comes up ("due soon") or has
    /// passed ("overdue"). Each of the four messages of a ticket (response soon / missed, solution soon / missed) goes
    /// out once; which ones went out is kept on the ticket (IssueTable.SlaNotices).
    ///
    /// Without it the targets would only be colours on a list that somebody has to look at.
    /// </summary>
    public sealed class SlaWatcher : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<SlaWatcher> _logger;
        private readonly TimeSpan _every;

        public SlaWatcher(IServiceScopeFactory scopes, ILogger<SlaWatcher> logger, IConfiguration configuration)
        {
            _scopes = scopes;
            _logger = logger;
            _every = TimeSpan.FromSeconds(Math.Clamp(configuration.GetValue("Sla:CheckSeconds", 60), 5, 3600));
        }

        /// <summary>When the last round finished, and how many messages it caused (for the system health page).</summary>
        public DateTime? LastRun { get; private set; }
        public string? LastError { get; private set; }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // let the application start (and the database updates run) before the first round
            try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); } catch (OperationCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                await RunOnceAsync(stoppingToken);
                try { await Task.Delay(_every, stoppingToken); } catch (OperationCanceledException) { return; }
            }
        }

        /// <summary>One round. Returns how many messages were caused. Never throws: the next round tries again.</summary>
        public async Task<int> RunOnceAsync(CancellationToken cancel = default)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var sla = scope.ServiceProvider.GetRequiredService<SlaService>();
                if (!sla.Policy.IsOn)
                {
                    LastRun = DateTime.Now;
                    LastError = null;
                    return 0;
                }

                var db = scope.ServiceProvider.GetRequiredService<DataContext>();
                var notify = scope.ServiceProvider.GetRequiredService<NotificationService>();
                var now = DateTime.Now;

                // open tickets that still have a target to reach
                var candidates = await db.Issues
                    .Where(issue => issue.IStatus != TicketStatus.Closed
                        && ((issue.ResponseDueAt != null && issue.FirstResponseAt == null) || (issue.ResolveDueAt != null && issue.ResolvedAt == null)))
                    .ToListAsync(cancel);

                var sent = 0;
                foreach (var issue in candidates)
                {
                    var flags = (SlaNotice)issue.SlaNotices;
                    var before = flags;

                    if (issue.ResponseDueAt != null && issue.FirstResponseAt == null && !SlaService.IsSolved(issue.IStatus))
                    {
                        flags = Check(notify, sla, issue, true, issue.ResponseDueAt.Value, now, flags, SlaNotice.ResponseSoon, SlaNotice.ResponseMissed);
                    }

                    // the clock for the solution stands still while the ticket is pending
                    if (issue.ResolveDueAt != null && issue.ResolvedAt == null && issue.PendingSince == null)
                    {
                        flags = Check(notify, sla, issue, false, issue.ResolveDueAt.Value, now, flags, SlaNotice.ResolveSoon, SlaNotice.ResolveMissed);
                    }

                    if (flags != before)
                    {
                        issue.SlaNotices = (int)flags;
                        sent++;
                    }
                }

                if (sent > 0)
                {
                    await db.SaveChangesAsync(cancel);
                }

                LastRun = DateTime.Now;
                LastError = null;
                return sent;
            }
            catch (OperationCanceledException)
            {
                return 0;
            }
            catch (Exception exception)
            {
                LastError = exception.GetBaseException().Message;
                _logger.LogWarning(exception, "The check of the service targets failed; it runs again in {Seconds} seconds", (int)_every.TotalSeconds);
                return 0;
            }
        }

        private static SlaNotice Check(NotificationService notify, SlaService sla, IssueTable issue, bool response, DateTime due, DateTime now,
            SlaNotice flags, SlaNotice soon, SlaNotice missed)
        {
            if (now > due)
            {
                if (!flags.HasFlag(missed))
                {
                    notify.SlaAlert(issue, response, true, "was due on " + due.ToString("d MMM, HH:mm") + " (" + SlaService.Span(now - due) + " ago)");
                    // "soon" is pointless once the target has passed
                    flags |= missed | soon;
                }
            }
            else if (!flags.HasFlag(soon) && now >= sla.WarnFrom(issue.TDate, due))
            {
                notify.SlaAlert(issue, response, false, "is due on " + due.ToString("d MMM, HH:mm") + " (in " + SlaService.Span(due - now) + ")");
                flags |= soon;
            }

            return flags;
        }
    }
}
