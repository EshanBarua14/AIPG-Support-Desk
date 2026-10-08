namespace XFLCSMS.Models.Admin
{
    /// <summary>
    /// The numbers behind the charts of the dashboard and of the ticket report, for one period.
    /// Built by Services/TicketStats.cs; sent to the page as JSON and drawn by wwwroot/js/charts.js.
    /// </summary>
    public class TicketCharts
    {
        /// <summary>Length of the period in days (dashboard: 7, 30, 90 or 365).</summary>
        public int Days { get; set; }
        /// <summary>"Last 30 days", or the dates of a report.</summary>
        public string PeriodName { get; set; } = string.Empty;
        /// <summary>What one point of the trend covers: "day", "week" or "month".</summary>
        public string Bucket { get; set; } = "day";

        public int Raised { get; set; }
        public int Closed { get; set; }
        /// <summary>Raised in the period and not closed yet.</summary>
        public int StillOpen { get; set; }
        /// <summary>The same numbers for the period of equal length right before; null when there is none to compare with.</summary>
        public int? PreviousRaised { get; set; }
        public int? PreviousClosed { get; set; }
        /// <summary>Half of the tickets closed in the period took at most this long, in hours; null when none was closed.</summary>
        public double? MedianHours { get; set; }
        public double? PreviousMedianHours { get; set; }

        public List<TrendPoint> Trend { get; set; } = new();
        public List<SplitRow> Products { get; set; } = new();
        public List<SplitRow> Priorities { get; set; } = new();
        public List<SplitRow> Houses { get; set; } = new();
        public List<CountRow> CloseTimes { get; set; } = new();
        /// <summary>Open tickets per support engineer right now (not limited to the period); empty when the role may not see the workload.</summary>
        public List<CountRow> Engineers { get; set; } = new();
    }

    public class TrendPoint
    {
        /// <summary>Short text under the axis ("8 Oct").</summary>
        public string Label { get; set; } = string.Empty;
        /// <summary>Full text for the tooltip and the table ("Thu 8 Oct 2026", "2 - 8 Oct 2026").</summary>
        public string Title { get; set; } = string.Empty;
        public int Raised { get; set; }
        public int Closed { get; set; }
    }

    /// <summary>One bar split into tickets that are not closed and tickets that are.</summary>
    public class SplitRow
    {
        public string Name { get; set; } = string.Empty;
        public int Open { get; set; }
        public int Closed { get; set; }
    }

    public class CountRow
    {
        public string Name { get; set; } = string.Empty;
        public int Count { get; set; }
    }
}
