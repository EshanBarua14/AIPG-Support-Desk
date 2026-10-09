using System.Globalization;
using XFLCSMS.Services;

namespace XFLCSMS.Models.Desk
{
    /// <summary>The page System &gt; Service targets: what it shows and what it posts.</summary>
    public class ServiceTargetsForm
    {
        public static readonly string[] Priorities = { "High", "Medium", "Low" };

        public bool Enabled { get; set; }

        /// <summary>
        /// Hours until the first response, per priority (High, Medium, Low), as typed: "0.5", "4". 0 or empty: no target.
        /// Text on purpose: the browser always sends a dot, whatever number format the server is set to.
        /// </summary>
        public Dictionary<string, string?> ResponseHours { get; set; } = new();

        /// <summary>Hours until the solution, per priority.</summary>
        public Dictionary<string, string?> ResolveHours { get; set; } = new();

        /// <summary>The number in a field, or null when it is not a number. Empty counts as 0 (no target).</summary>
        public static decimal? Number(Dictionary<string, string?> fields, string priority)
        {
            var text = (fields.GetValueOrDefault(priority) ?? string.Empty).Trim().Replace(',', '.');
            if (text.Length == 0) { return 0; }
            return decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) ? value : null;
        }

        /// <summary>"business" (working time) or "always" (round the clock), per priority.</summary>
        public Dictionary<string, string?> Clock { get; set; } = new();

        /// <summary>Working days as numbers, 0 = Sunday ... 6 = Saturday.</summary>
        public List<int> Days { get; set; } = new();

        public string? Start { get; set; }
        public string? End { get; set; }

        /// <summary>One date per line, 2026-12-16, optionally followed by what the day is.</summary>
        public string? Holidays { get; set; }

        /// <summary>Percent of the time after which "due soon" is reported.</summary>
        public int WarnPercent { get; set; } = 75;

        public static ServiceTargetsForm From(SlaPolicy policy, SettingsStore settings)
        {
            var calendar = policy.Calendar;
            var form = new ServiceTargetsForm
            {
                Enabled = policy.IsOn,
                Days = calendar.WorkDays.Select(day => (int)day).OrderBy(day => day).ToList(),
                Start = calendar.Start.ToString("hh\\:mm", CultureInfo.InvariantCulture),
                End = calendar.End.ToString("hh\\:mm", CultureInfo.InvariantCulture),
                Holidays = settings.Get(SlaPolicy.HolidayList),
                WarnPercent = policy.WarnPercent
            };
            foreach (var priority in Priorities)
            {
                form.ResponseHours[priority] = Math.Round(policy.ResponseMinutes(priority) / 60m, 2).ToString("0.##", CultureInfo.InvariantCulture);
                form.ResolveHours[priority] = Math.Round(policy.ResolveMinutes(priority) / 60m, 2).ToString("0.##", CultureInfo.InvariantCulture);
                form.Clock[priority] = policy.RoundTheClock(priority) ? "always" : "business";
            }

            return form;
        }

        /// <summary>Null when the form can be saved, otherwise what to correct.</summary>
        public string? Problem()
        {
            foreach (var priority in Priorities)
            {
                var response = Number(ResponseHours, priority);
                var resolve = Number(ResolveHours, priority);
                if (response == null || resolve == null || response < 0 || resolve < 0 || response > 8760 || resolve > 8760)
                {
                    return "Times are hours between 0 and 8760 (0 = no target). Check " + priority + ".";
                }

                if (response > 0 && resolve > 0 && response > resolve)
                {
                    return priority + ": the first response cannot be later than the solution.";
                }
            }

            if (Days.Count == 0 || Days.Any(day => day < 0 || day > 6))
            {
                return "Choose at least one working day.";
            }

            if (!TimeSpan.TryParseExact(Start, "hh\\:mm", CultureInfo.InvariantCulture, out var start)
                || !TimeSpan.TryParseExact(End, "hh\\:mm", CultureInfo.InvariantCulture, out var end))
            {
                return "Enter the working hours as 09:00 and 18:00.";
            }

            if (end <= start)
            {
                return "The working day has to end after it starts.";
            }

            if (WarnPercent < 10 || WarnPercent > 95)
            {
                return "The warning comes between 10 and 95 percent of the time.";
            }

            // a line that is not a date would silently not be a holiday
            foreach (var line in (Holidays ?? string.Empty).Split(new[] { '\n', '\r', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!DateTime.TryParseExact(line.Split(' ', 2)[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                {
                    return "Holidays: “" + line + "” does not start with a date like 2026-12-16.";
                }
            }

            return null;
        }

        public Dictionary<string, string?> ToSettings()
        {
            var values = new Dictionary<string, string?>
            {
                [SlaPolicy.Enabled] = Enabled ? "true" : "false",
                [SlaPolicy.Days] = string.Join(",", Days.Distinct().OrderBy(day => day)),
                [SlaPolicy.DayStart] = Start,
                [SlaPolicy.DayEnd] = End,
                [SlaPolicy.HolidayList] = string.IsNullOrWhiteSpace(Holidays) ? null : Holidays.Replace("\r\n", "\n").Trim(),
                [SlaPolicy.WarnAt] = WarnPercent.ToString(CultureInfo.InvariantCulture)
            };
            foreach (var priority in Priorities)
            {
                // "0" is a value of its own ("no target"), so it is stored and not left to the default
                values[SlaPolicy.ResponseKey(priority)] = Minutes(Number(ResponseHours, priority));
                values[SlaPolicy.ResolveKey(priority)] = Minutes(Number(ResolveHours, priority));
                values[SlaPolicy.ClockKey(priority)] = Clock.GetValueOrDefault(priority) == "always" ? "always" : "business";
            }

            return values;
        }

        private static string Minutes(decimal? hours)
        {
            return ((int)Math.Round((hours ?? 0) * 60m)).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>The settings in one sentence, for the audit trail.</summary>
        public string Describe()
        {
            var parts = Priorities.Select(priority => priority + " " + Text(Number(ResponseHours, priority)) + " / " + Text(Number(ResolveHours, priority))
                + (Clock.GetValueOrDefault(priority) == "always" ? " round the clock" : string.Empty));
            var names = new[] { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };
            return (Enabled ? "on" : "off") + "; " + string.Join(", ", parts) + "; working time " + string.Join(" ", Days.OrderBy(day => day).Select(day => names[day])) + " " + Start + "-" + End
                + "; " + SlaPolicy.ParseDays(Holidays).Count + " holidays; warning at " + WarnPercent + "%.";
        }

        private static string Text(decimal? hours)
        {
            return hours == null || hours == 0 ? "none" : hours.Value.ToString("0.##", CultureInfo.InvariantCulture) + " h";
        }
    }
}
