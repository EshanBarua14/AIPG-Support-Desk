using XFLCSMS.Models.Issue;

namespace XFLCSMS.Models.Admin
{
    /// <summary>Numbers for the dashboard of one role. Built in CsmsController.BuildDashboardAsync.</summary>
    public class Dashboard
    {
        // All time. "Queue" = every ticket that is not closed yet.
        public int? TotalTicket { get; set; }
        public int? TotalClosed { get; set; }
        public int? TotalQueue { get; set; }

        // Tickets raised today / in the last 7 days / month / year, and how many of those are closed.
        public int? TodayTotalTicket { get; set; }
        public int? TodayTotalClosed { get; set; }
        public int? TodayTotalQueue { get; set; }
        public int? WeeklyTotalTicket { get; set; }
        public int? WeeklyTotalClosed { get; set; }
        public int? WeeklyTotalQueue { get; set; }
        public int? MonthlyTotalTicket { get; set; }
        public int? MonthlyTotalClosed { get; set; }
        public int? MonthlyTotalQueue { get; set; }
        public int? YearlyTotalTicket { get; set; }
        public int? YearlyTotalClosed { get; set; }
        public int? YearlyTotalQueue { get; set; }

        /// <summary>Tickets per status (key: the stored status, see Services/TicketStatus.cs).</summary>
        public Dictionary<string, int> ByStatus { get; set; } = new();

        /// <summary>Open tickets nobody is assigned to.</summary>
        public int Unassigned { get; set; }

        /// <summary>Open tickets with priority High.</summary>
        public int HighPriorityOpen { get; set; }

        /// <summary>Open tickets assigned to the signed-in user (support engineers).</summary>
        public int AssignedToMe { get; set; }

        /// <summary>Tickets per brokerage house, busiest first. Empty for makers (they only see their own tickets).</summary>
        public List<HouseLoad> Houses { get; set; } = new();

        /// <summary>The newest tickets the user may see.</summary>
        public List<IssueTable> Recent { get; set; } = new();

        /// <summary>Open tickets waiting for an engineer, oldest first.</summary>
        public List<IssueTable> Waiting { get; set; } = new();

        /// <summary>The numbers behind the charts, for the period the page opens with (CsmsController.BuildChartsAsync).</summary>
        public TicketCharts Charts { get; set; } = new();
    }

    public class HouseLoad
    {
        public string Name { get; set; } = string.Empty;
        public string Acronym { get; set; } = string.Empty;
        public int Open { get; set; }
        public int Closed { get; set; }
        public int Total => Open + Closed;
    }
}
