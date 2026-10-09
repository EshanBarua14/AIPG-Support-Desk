using XFLCSMS.Services;

namespace XFLCSMS.Models.Desk
{
    /// <summary>The page System &gt; Automation: what it shows and what it posts.</summary>
    public class AutomationForm
    {
        /// <summary>"off", "round" or "least" (AutomationRules).</summary>
        public string? Assign { get; set; }
        public int CloseDays { get; set; }
        public int RemindDays { get; set; }
        public int RemindMax { get; set; } = 2;
        public int PendingCloseDays { get; set; }

        public static AutomationForm From(AutomationRules rules)
        {
            return new AutomationForm
            {
                Assign = rules.AssignMode, CloseDays = rules.CloseDays, RemindDays = rules.RemindDays, RemindMax = rules.RemindMax, PendingCloseDays = rules.PendingCloseDays
            };
        }

        /// <summary>Null when the form can be saved, otherwise what to correct.</summary>
        public string? Problem()
        {
            if (Assign != AutomationRules.Off && Assign != AutomationRules.RoundRobin && Assign != AutomationRules.LeastLoad)
            {
                return "Choose who gets a new ticket.";
            }

            if (CloseDays < 0 || CloseDays > 365 || RemindDays < 0 || RemindDays > 365 || PendingCloseDays < 0 || PendingCloseDays > 365)
            {
                return "Days are numbers between 0 and 365 (0 = never).";
            }

            if (RemindMax < 1 || RemindMax > 10)
            {
                return "Between 1 and 10 reminders.";
            }

            if (PendingCloseDays > 0 && RemindDays > 0 && PendingCloseDays <= RemindDays)
            {
                return "A pending ticket would be closed before its first reminder: close it later than the first reminder (" + RemindDays + " days), or switch the reminders off.";
            }

            return null;
        }

        public Dictionary<string, string?> ToSettings()
        {
            var number = System.Globalization.CultureInfo.InvariantCulture;
            return new Dictionary<string, string?>
            {
                [AutomationRules.AssignKey] = Assign,
                [AutomationRules.CloseDaysKey] = CloseDays.ToString(number),
                [AutomationRules.RemindDaysKey] = RemindDays.ToString(number),
                [AutomationRules.RemindMaxKey] = RemindMax.ToString(number),
                [AutomationRules.PendingCloseDaysKey] = PendingCloseDays.ToString(number)
            };
        }

        /// <summary>The rules in one sentence, for the audit trail.</summary>
        public string Describe()
        {
            return "new tickets: " + (Assign == AutomationRules.RoundRobin ? "to the engineers in turn" : Assign == AutomationRules.LeastLoad ? "to the engineer with the fewest open tickets" : "not assigned automatically")
                + "; close after Deployed: " + Days(CloseDays)
                + "; reminder while Pending: " + (RemindDays == 0 ? "never" : "every " + Days(RemindDays) + ", at most " + RemindMax + " times")
                + "; close while Pending: " + Days(PendingCloseDays) + ".";
        }

        private static string Days(int days)
        {
            return days == 0 ? "never" : days == 1 ? "1 day" : days + " days";
        }
    }
}
