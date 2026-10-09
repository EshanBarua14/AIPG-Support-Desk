using XFLCSMS.Models.Brocarage;

namespace XFLCSMS.Models.Desk
{
    /// <summary>The service report: how fast and how well support answered in a period. Built by ServiceReportBuilder.</summary>
    public class ServiceReport
    {
        public int Days { get; set; }
        public int HouseId { get; set; }
        public string? HouseName { get; set; }
        public List<Brokerage> Houses { get; set; } = new();
        /// <summary>An engineer without "work on any ticket" sees the own tickets only.</summary>
        public bool OwnWorkOnly { get; set; }

        // the period
        public int Raised { get; set; }
        public int Solved { get; set; }
        public int Reopened { get; set; }
        /// <summary>Working minutes from raised to the first response; half of the tickets were faster.</summary>
        public double? MedianResponse { get; set; }
        public double? MedianResolve { get; set; }
        public Share ResponseMet { get; set; } = new();
        public Share ResolveMet { get; set; } = new();
        public double? AverageRating { get; set; }
        public int Ratings { get; set; }
        /// <summary>How many tickets got 1, 2, 3, 4, 5 (index 0 to 4).</summary>
        public int[] RatingCounts { get; set; } = new int[5];

        // now
        public int OpenNow { get; set; }
        public int OverdueNow { get; set; }
        public int WaitingForResponse { get; set; }
        /// <summary>Open tickets by age: younger than a day, 1 to 3 days, 3 to 7 days, older.</summary>
        public List<(string Name, int Count)> Backlog { get; set; } = new();

        public List<Row> ByPriority { get; set; } = new();
        public List<Row> ByEngineer { get; set; } = new();
        public List<Row> ByHouse { get; set; } = new();
        /// <summary>Per product of XFL; tickets raised without a product come last, under a name of their own.</summary>
        public List<Row> ByProduct { get; set; } = new();
        /// <summary>Raised and solved per day (short periods) or per week.</summary>
        public List<(string Label, int Raised, int Solved)> Trend { get; set; } = new();
        public bool TrendIsWeekly { get; set; }

        /// <summary>A target met by <see cref="Met"/> of <see cref="Of"/> tickets (tickets still inside their time are not counted yet).</summary>
        public class Share
        {
            public int Met { get; set; }
            public int Of { get; set; }
            public int? Percent => Of == 0 ? null : (int)Math.Round(100.0 * Met / Of);
        }

        public class Row
        {
            public string Name { get; set; } = string.Empty;
            public int Raised { get; set; }
            public int Solved { get; set; }
            public double? MedianResponse { get; set; }
            public double? MedianResolve { get; set; }
            public Share ResponseMet { get; set; } = new();
            public Share ResolveMet { get; set; } = new();
            public double? AverageRating { get; set; }
            public int Ratings { get; set; }
            public int OpenNow { get; set; }
        }
    }
}
