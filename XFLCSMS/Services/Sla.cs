using System.Globalization;
using XFLCSMS.Models.Issue;

namespace XFLCSMS.Services
{
    /// <summary>
    /// The working time of the support team: which days of the week, from when to when, and the days off in between.
    /// Service targets count in this time: a ticket raised on Thursday evening with "answer within 2 hours" is due
    /// on Sunday morning, not in the middle of the night.
    /// </summary>
    public sealed class BusinessCalendar
    {
        public BusinessCalendar(IEnumerable<DayOfWeek> workDays, TimeSpan start, TimeSpan end, IEnumerable<DateTime>? holidays)
        {
            WorkDays = new HashSet<DayOfWeek>(workDays);
            Start = start;
            End = end;
            Holidays = new HashSet<DateTime>((holidays ?? Enumerable.Empty<DateTime>()).Select(day => day.Date));
        }

        public HashSet<DayOfWeek> WorkDays { get; }
        public TimeSpan Start { get; }
        public TimeSpan End { get; }
        public HashSet<DateTime> Holidays { get; }

        /// <summary>A calendar nobody can work in (no day, or the day ends before it starts) counts round the clock instead.</summary>
        public bool IsUsable => WorkDays.Count > 0 && End > Start;

        public int MinutesPerDay => IsUsable ? (int)(End - Start).TotalMinutes : 24 * 60;

        public bool IsWorkingDay(DateTime day)
        {
            return WorkDays.Contains(day.DayOfWeek) && !Holidays.Contains(day.Date);
        }

        /// <summary>The moment that lies the given number of working minutes after <paramref name="from"/>.</summary>
        public DateTime Add(DateTime from, int minutes)
        {
            if (!IsUsable || minutes <= 0)
            {
                return from.AddMinutes(Math.Max(minutes, 0));
            }

            var cursor = from;
            var left = (double)minutes;
            // ten years of days is far more than any target; the bound only makes an endless loop impossible
            for (var guard = 0; guard < 3660; guard++)
            {
                var open = cursor.Date + Start;
                var close = cursor.Date + End;
                if (IsWorkingDay(cursor) && cursor < close)
                {
                    if (cursor < open) { cursor = open; }
                    var available = (close - cursor).TotalMinutes;
                    if (left <= available)
                    {
                        return cursor.AddMinutes(left);
                    }

                    left -= available;
                }

                cursor = cursor.Date.AddDays(1) + Start;
            }

            return cursor;
        }

        /// <summary>Working minutes between two moments (0 when the second is not after the first).</summary>
        public int MinutesBetween(DateTime from, DateTime until)
        {
            if (until <= from)
            {
                return 0;
            }

            if (!IsUsable)
            {
                return (int)(until - from).TotalMinutes;
            }

            double total = 0;
            var day = from.Date;
            for (var guard = 0; guard < 3660 && day <= until.Date; guard++, day = day.AddDays(1))
            {
                if (!IsWorkingDay(day))
                {
                    continue;
                }

                var open = day + Start;
                var close = day + End;
                var a = from > open ? from : open;
                var b = until < close ? until : close;
                if (b > a)
                {
                    total += (b - a).TotalMinutes;
                }
            }

            return (int)Math.Round(total);
        }
    }

    /// <summary>Bits of IssueTable.SlaNotices: which warnings about the targets were already sent.</summary>
    [Flags]
    public enum SlaNotice
    {
        None = 0,
        ResponseSoon = 1,
        ResponseMissed = 2,
        ResolveSoon = 4,
        ResolveMissed = 8
    }

    /// <summary>
    /// The service targets as they are set on System &gt; Service targets (stored in AppSettings, keys "sla.*"):
    /// per priority the time to the first response and to the solution, whether the clock runs in working time
    /// or round the clock, and the working time itself.
    /// </summary>
    public sealed class SlaPolicy
    {
        public const string Enabled = "sla.enabled";
        public const string WarnAt = "sla.warn";
        public const string Days = "sla.days";
        public const string DayStart = "sla.start";
        public const string DayEnd = "sla.end";
        public const string HolidayList = "sla.holidays";
        public static string ResponseKey(string priority) => "sla.response." + priority;
        public static string ResolveKey(string priority) => "sla.resolve." + priority;
        public static string ClockKey(string priority) => "sla.clock." + priority;

        /// <summary>What applies until somebody saves the page: (first response, solution) in working minutes.</summary>
        public static readonly IReadOnlyDictionary<string, (int Response, int Resolve)> DefaultTargets = new Dictionary<string, (int, int)>
        {
            ["High"] = (30, 4 * 60),
            ["Medium"] = (2 * 60, 2 * 9 * 60),
            ["Low"] = (4 * 60, 5 * 9 * 60)
        };

        public const string DefaultDays = "0,1,2,3,4";   // Sunday to Thursday
        public const string DefaultStart = "09:00";
        public const string DefaultEnd = "18:00";

        private readonly SettingsStore _settings;

        public SlaPolicy(SettingsStore settings)
        {
            _settings = settings;
        }

        public bool IsOn => _settings.GetBool(Enabled, true);

        /// <summary>Percent of the time to a target after which the warning "soon due" goes out.</summary>
        public int WarnPercent => Math.Clamp(_settings.GetInt(WarnAt, 75), 10, 95);

        public int ResponseMinutes(string? priority) => Minutes(ResponseKey(Known(priority)), DefaultTargets[Known(priority)].Response);

        public int ResolveMinutes(string? priority) => Minutes(ResolveKey(Known(priority)), DefaultTargets[Known(priority)].Resolve);

        /// <summary>True: the clock of this priority runs day and night, every day. False: in working time.</summary>
        public bool RoundTheClock(string? priority) => _settings.Get(ClockKey(Known(priority)), "business") == "always";

        public BusinessCalendar Calendar
        {
            get
            {
                var days = (_settings.Get(Days, DefaultDays) ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(text => int.TryParse(text, out var number) && number >= 0 && number <= 6 ? (DayOfWeek?)number : null)
                    .Where(day => day != null).Select(day => day!.Value);
                return new BusinessCalendar(days, Time(DayStart, DefaultStart), Time(DayEnd, DefaultEnd), Holidays);
            }
        }

        public List<DateTime> Holidays => ParseDays(_settings.Get(HolidayList));

        /// <summary>The calendar a priority counts in: the working time, or none at all (round the clock).</summary>
        public BusinessCalendar CalendarFor(string? priority)
        {
            return RoundTheClock(priority) ? AlwaysOpen : Calendar;
        }

        public static readonly BusinessCalendar AlwaysOpen = new(Array.Empty<DayOfWeek>(), TimeSpan.Zero, TimeSpan.Zero, null);

        /// <summary>Dates written one per line or separated by commas, as 2026-12-16. Anything else on a line is ignored.</summary>
        public static List<DateTime> ParseDays(string? text)
        {
            var days = new List<DateTime>();
            foreach (var part in (text ?? string.Empty).Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var first = part.Split(' ', 2)[0];
                if (DateTime.TryParseExact(first, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                {
                    days.Add(day.Date);
                }
            }

            return days.Distinct().OrderBy(day => day).ToList();
        }

        private TimeSpan Time(string key, string fallback)
        {
            return TimeSpan.TryParseExact(_settings.Get(key, fallback), "hh\\:mm", CultureInfo.InvariantCulture, out var time)
                ? time
                : TimeSpan.ParseExact(fallback, "hh\\:mm", CultureInfo.InvariantCulture);
        }

        private int Minutes(string key, int fallback)
        {
            // a target of 0 means "no target for this priority"
            return Math.Clamp(_settings.GetInt(key, fallback), 0, 60 * 24 * 365);
        }

        private static string Known(string? priority)
        {
            return priority != null && DefaultTargets.ContainsKey(priority) ? priority : "Medium";
        }
    }

    /// <summary>How one target of a ticket stands, for the badge on lists and the box on the ticket page.</summary>
    public sealed class SlaClock
    {
        /// <summary>none: no target; running; soon; overdue; met; missed; paused.</summary>
        public string State { get; set; } = "none";
        public string Text { get; set; } = string.Empty;
        public DateTime? Due { get; set; }
        public DateTime? ReachedAt { get; set; }
        public bool IsBad => State == "overdue" || State == "missed";
    }

    /// <summary>
    /// Keeps the service times of a ticket: sets the targets when it is raised, stamps the first response and the
    /// solution, stops the clock while the ticket is "Pending", and says how each target stands.
    /// The targets a ticket got stay with it: changing the settings later affects new tickets only.
    /// </summary>
    public sealed class SlaService
    {
        private readonly SlaPolicy _policy;

        public SlaService(SlaPolicy policy)
        {
            _policy = policy;
        }

        public SlaPolicy Policy => _policy;

        /// <summary>A new ticket: its two targets, counted from the moment it was raised.</summary>
        public void Start(IssueTable issue)
        {
            if (!_policy.IsOn)
            {
                return;
            }

            SetTargets(issue);
        }

        /// <summary>The priority changed: the targets follow the new priority, still counted from when the ticket was raised.</summary>
        public void PriorityChanged(IssueTable issue)
        {
            // a ticket from before the targets existed keeps having none
            if (issue.ResponseDueAt == null && issue.ResolveDueAt == null)
            {
                return;
            }

            SetTargets(issue);
            issue.SlaNotices = 0; // other targets: the warnings start again
        }

        private void SetTargets(IssueTable issue)
        {
            var calendar = _policy.CalendarFor(issue.Priority);
            var response = _policy.ResponseMinutes(issue.Priority);
            var resolve = _policy.ResolveMinutes(issue.Priority);
            issue.ResponseDueAt = response > 0 ? calendar.Add(issue.TDate, response) : null;
            issue.ResolveDueAt = resolve > 0 ? calendar.Add(issue.TDate, resolve + issue.SlaPausedMinutes) : null;
        }

        /// <summary>XFL staff answered where the house can read it, or started to work: the first response, once.</summary>
        public void Responded(IssueTable issue, DateTime at)
        {
            issue.FirstResponseAt ??= at;
        }

        /// <summary>The status went from <paramref name="oldStatus"/> to the one the ticket has now.</summary>
        public void StatusChanged(IssueTable issue, string? oldStatus, bool byStaff, DateTime at)
        {
            var before = TicketStatus.Normalize(oldStatus);
            var now = TicketStatus.Normalize(issue.IStatus);

            // "Pending" is waiting for somebody else: the clock for the solution stands still meanwhile
            if (before == TicketStatus.Pending && now != TicketStatus.Pending && issue.PendingSince != null)
            {
                var paused = _policy.CalendarFor(issue.Priority).MinutesBetween(issue.PendingSince.Value, at);
                issue.SlaPausedMinutes += paused;
                if (issue.ResolveDueAt != null && paused > 0)
                {
                    issue.ResolveDueAt = _policy.CalendarFor(issue.Priority).Add(issue.ResolveDueAt.Value, paused);
                }

                issue.PendingSince = null;
            }

            if (now == TicketStatus.Pending && before != TicketStatus.Pending)
            {
                issue.PendingSince = at;
            }

            if (byStaff && now == TicketStatus.InProgress)
            {
                Responded(issue, at);
            }

            var solved = IsSolved(now);
            if (solved && issue.ResolvedAt == null)
            {
                issue.ResolvedAt = at;
            }
            else if (!solved && IsSolved(before))
            {
                // reopened: it is not solved after all, and the target it once met is open again
                issue.ResolvedAt = null;
                issue.ReopenCount++;
            }
        }

        /// <summary>Done, Deployed and Closed: the engineer's work on the problem is over.</summary>
        public static bool IsSolved(string? status)
        {
            var key = TicketStatus.Normalize(status);
            return key == TicketStatus.Done || key == TicketStatus.Deployed || key == TicketStatus.Closed;
        }

        // ---- how a target stands ------------------------------------------------------------------------------

        public SlaClock Response(IssueTable issue, DateTime now)
        {
            return Clock(issue.TDate, issue.ResponseDueAt, issue.FirstResponseAt, false, now, "answered");
        }

        public SlaClock Resolve(IssueTable issue, DateTime now)
        {
            return Clock(issue.TDate, issue.ResolveDueAt, issue.ResolvedAt, issue.PendingSince != null, now, "solved");
        }

        /// <summary>The one line for a list: the target that needs attention first.</summary>
        public SlaClock Worst(IssueTable issue, DateTime now)
        {
            var response = Response(issue, now);
            var resolve = Resolve(issue, now);
            if (issue.FirstResponseAt == null && response.State != "none")
            {
                // not answered yet: that is what matters now, unless the solution is already late too
                return resolve.State == "overdue" ? Named(resolve, "Solution") : Named(response, "Response");
            }

            if (resolve.State == "none")
            {
                return response.State == "missed" ? Named(response, "Response") : resolve;
            }

            return Named(resolve, "Solution");
        }

        /// <summary>
        /// The same as <see cref="Worst"/> in as few words as a list has room for: "Reply due in 40 min",
        /// "Overdue 3 d", "On hold". Tickets without targets give nothing.
        /// </summary>
        public SlaClock Short(IssueTable issue, DateTime now)
        {
            var response = Response(issue, now);
            var resolve = Resolve(issue, now);
            var waitsForReply = issue.FirstResponseAt == null && response.Due != null && !IsSolved(issue.IStatus);
            if (waitsForReply && resolve.State != "overdue")
            {
                return new SlaClock
                {
                    State = response.State, Due = response.Due,
                    Text = response.State == "overdue" ? "Reply overdue " + Brief(now - response.Due!.Value) : "Reply due in " + Brief(response.Due!.Value - now)
                };
            }

            if (resolve.Due == null)
            {
                return new SlaClock();
            }

            string text;
            switch (resolve.State)
            {
                case "overdue": text = "Overdue " + Brief(now - resolve.Due.Value); break;
                case "paused": text = "On hold"; break;
                case "met": text = "Solved in time"; break;
                case "missed": text = "Solved late"; break;
                default: text = "Due in " + Brief(resolve.Due.Value - now); break;
            }

            return new SlaClock { State = resolve.State, Due = resolve.Due, ReachedAt = resolve.ReachedAt, Text = text };
        }

        /// <summary>A length of time in its largest unit only: "40 min", "3 h", "18 d".</summary>
        public static string Brief(TimeSpan time)
        {
            if (time < TimeSpan.Zero) { time = time.Negate(); }
            if (time.TotalMinutes < 60) { return Math.Max(1, (int)time.TotalMinutes) + " min"; }
            if (time.TotalHours < 48) { return (int)time.TotalHours + " h"; }
            return (int)time.TotalDays + " d";
        }

        private static SlaClock Named(SlaClock clock, string what)
        {
            return new SlaClock { State = clock.State, Due = clock.Due, ReachedAt = clock.ReachedAt, Text = what + " " + clock.Text };
        }

        private SlaClock Clock(DateTime start, DateTime? due, DateTime? reached, bool paused, DateTime now, string verb)
        {
            if (due == null)
            {
                return reached == null
                    ? new SlaClock()
                    : new SlaClock { State = "none", ReachedAt = reached, Text = verb + " after " + Span(reached.Value - start) };
            }

            if (reached != null)
            {
                return reached <= due
                    ? new SlaClock { State = "met", Due = due, ReachedAt = reached, Text = verb + " in time" }
                    : new SlaClock { State = "missed", Due = due, ReachedAt = reached, Text = verb + " " + Span(reached.Value - due.Value) + " late" };
            }

            if (paused)
            {
                return new SlaClock { State = "paused", Due = due, Text = "on hold while pending" };
            }

            if (now > due)
            {
                return new SlaClock { State = "overdue", Due = due, Text = "overdue by " + Span(now - due.Value) };
            }

            // "soon": the same moment the warning goes out
            return new SlaClock { State = now >= WarnFrom(start, due.Value) ? "soon" : "running", Due = due, Text = "due in " + Span(due.Value - now) };
        }

        /// <summary>The moment from which a target counts as "soon due" (see <see cref="SlaPolicy.WarnPercent"/>).</summary>
        public DateTime WarnFrom(DateTime start, DateTime due)
        {
            return start + TimeSpan.FromTicks((long)((due - start).Ticks * (_policy.WarnPercent / 100.0)));
        }

        /// <summary>A length of time the way people say it: "25 min", "3 h 10 min", "2 d 4 h".</summary>
        public static string Span(TimeSpan time)
        {
            if (time < TimeSpan.Zero) { time = time.Negate(); }
            if (time.TotalMinutes < 1) { return "less than a minute"; }
            if (time.TotalMinutes < 60) { return (int)time.TotalMinutes + " min"; }
            if (time.TotalHours < 24) { return (int)time.TotalHours + " h" + (time.Minutes > 0 ? " " + time.Minutes + " min" : string.Empty); }
            return (int)time.TotalDays + " d" + (time.Hours > 0 ? " " + time.Hours + " h" : string.Empty);
        }

        /// <summary>Minutes for a report: "25 min", "3.2 h", "26 h".</summary>
        public static string Hours(double? minutes)
        {
            if (minutes == null) { return "-"; }
            if (minutes.Value < 60) { return Math.Round(minutes.Value).ToString("0", CultureInfo.InvariantCulture) + " min"; }
            var hours = minutes.Value / 60.0;
            return (hours < 10 ? hours.ToString("0.#", CultureInfo.InvariantCulture) : Math.Round(hours).ToString("0", CultureInfo.InvariantCulture)) + " h";
        }
    }
}
