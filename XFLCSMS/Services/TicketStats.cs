using System.Globalization;
using System.Text.Json;
using XFLCSMS.Models.Admin;

namespace XFLCSMS.Services
{
    /// <summary>
    /// Turns a set of tickets into the numbers the charts show (Models/Admin/TicketCharts.cs). No database access
    /// here: the caller decides which tickets count (CsmsController.VisibleIssues, or the result of a report).
    /// </summary>
    public static class TicketStats
    {
        /// <summary>What the charts need of one ticket.</summary>
        public class Row
        {
            public int IssueId { get; set; }
            public int BrokerageId { get; set; }
            public int? ProductId { get; set; }
            public string? Status { get; set; }
            public string? Priority { get; set; }
            public DateTime Raised { get; set; }
            public DateTime? ClosedOn { get; set; }
            public DateTime? UpdatedOn { get; set; }
            public string? AssignedTo { get; set; }

            public bool IsClosed => Status == TicketStatus.Closed;

            /// <summary>When the ticket was closed. Tickets closed by old versions have no date: the last change counts, else the day they were raised.</summary>
            public DateTime ClosedAt => ClosedOn ?? UpdatedOn ?? Raised;
        }

        /// <summary>The periods the dashboard offers, in days.</summary>
        public static readonly int[] Periods = { 7, 30, 90, 365 };

        public static int CleanDays(int days)
        {
            return Array.IndexOf(Periods, days) >= 0 ? days : 30;
        }

        public static string PeriodName(int days)
        {
            return days == 365 ? "Last 12 months" : "Last " + days + " days";
        }

        /// <summary>
        /// First day of a period that ends today. 7, 30 and 90 days count back from today (today included); the
        /// 12 months are whole calendar months - this month and the eleven before - so the chart has one point per month.
        /// </summary>
        public static DateTime PeriodStart(int days, DateTime today)
        {
            today = today.Date;
            return days == 365 ? new DateTime(today.Year, today.Month, 1).AddMonths(-11) : today.AddDays(-(days - 1));
        }

        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private static readonly CultureInfo En = CultureInfo.InvariantCulture;

        /// <param name="rows">Every ticket that may count (also older ones: the period before is compared with).</param>
        /// <param name="from">First day of the period.</param>
        /// <param name="to">Last day of the period (today for the dashboard).</param>
        /// <param name="compare">Also fill the numbers of the period right before.</param>
        /// <param name="products">Names of the products by id.</param>
        /// <param name="houses">Names of the brokerage houses by id; null: no chart per house (the role sees one house only).</param>
        /// <param name="engineers">Fill the open tickets per engineer.</param>
        /// <param name="closedWhenever">
        /// The rows are the result of a report (chosen by the day they were raised): "closed" and the time to close then
        /// count every closed ticket of the rows, also one closed after <paramref name="to"/> - as the report itself does.
        /// </param>
        public static TicketCharts Build(IReadOnlyCollection<Row> rows, DateTime from, DateTime to, bool compare,
            IReadOnlyDictionary<int, string> products, IReadOnlyDictionary<int, string>? houses, bool engineers, bool closedWhenever = false)
        {
            from = from.Date;
            var end = to.Date.AddDays(1);                     // exclusive
            if (end <= from) { end = from.AddDays(1); }
            var days = (int)(end - from).TotalDays;
            var before = from.AddDays(-days);

            var raised = rows.Where(r => r.Raised >= from && r.Raised < end).ToList();
            var closed = rows.Where(r => r.IsClosed && r.ClosedAt >= from && r.ClosedAt < end).ToList();   // closed inside the period: the line of the chart
            var closedCounted = closedWhenever ? rows.Where(r => r.IsClosed).ToList() : closed;             // what "closed" and "time to close" count

            var charts = new TicketCharts
            {
                Days = days,
                Raised = raised.Count,
                Closed = closedCounted.Count,
                StillOpen = raised.Count(r => !r.IsClosed),
                MedianHours = Median(closedCounted)
            };

            if (compare)
            {
                var closedBefore = rows.Where(r => r.IsClosed && r.ClosedAt >= before && r.ClosedAt < from).ToList();
                charts.PreviousRaised = rows.Count(r => r.Raised >= before && r.Raised < from);
                charts.PreviousClosed = closedBefore.Count;
                charts.PreviousMedianHours = Median(closedBefore);
            }

            // ---- raised and closed over time: per day up to a month, per week up to a quarter, else per month
            charts.Bucket = days <= 31 ? "day" : days <= 92 ? "week" : "month";
            foreach (var (start, stop) in Buckets(from, end, charts.Bucket))
            {
                var last = stop.AddDays(-1);
                charts.Trend.Add(new TrendPoint
                {
                    // a year under the first month and under every January, so "Oct" at both ends cannot be mixed up
                    Label = charts.Bucket != "month" ? start.ToString("d MMM", En)
                        : start.ToString(charts.Trend.Count == 0 || start.Month == 1 ? "MMM yyyy" : "MMM", En),
                    Title = charts.Bucket == "day" ? start.ToString("ddd d MMM yyyy", En)
                        : charts.Bucket == "month" && start.Day == 1 && stop.Day == 1 ? start.ToString("MMMM yyyy", En)
                        : start == last ? start.ToString("d MMM yyyy", En)
                        : start.ToString(start.Year == last.Year ? "d MMM" : "d MMM yyyy", En) + " – " + last.ToString("d MMM yyyy", En),
                    Raised = raised.Count(r => r.Raised >= start && r.Raised < stop),
                    Closed = closed.Count(r => r.ClosedAt >= start && r.ClosedAt < stop)
                });
            }

            // ---- what the tickets raised in the period are about
            charts.Products = Split(raised, r => r.ProductId, id => id == null ? "No product" : products.TryGetValue(id.Value, out var name) ? name : "Deleted product", 6, "Other products");
            if (charts.Products.Count == 1 && charts.Products[0].Name == "No product" && products.Count == 0)
            {
                charts.Products.Clear();   // an installation without products: nothing to compare
            }

            charts.Priorities = Enumerable.Reverse(TicketService.Priorities)   // High first
                .Select(priority => new SplitRow
                {
                    Name = priority,
                    Open = raised.Count(r => r.Priority == priority && !r.IsClosed),
                    Closed = raised.Count(r => r.Priority == priority && r.IsClosed)
                })
                .ToList();

            if (houses != null)
            {
                charts.Houses = Split(raised, r => (int?)r.BrokerageId, id => id != null && houses.TryGetValue(id.Value, out var name) ? name : "Unknown house", 7, "Other houses");
            }

            // ---- how long closing took
            var edges = new (string Name, double UpToHours)[]
            {
                ("Under 1 day", 24), ("1 to 3 days", 72), ("3 to 7 days", 168), ("1 to 4 weeks", 672), ("Over 4 weeks", double.MaxValue)
            };
            var hours = closedCounted.Select(Hours).ToList();
            var lower = 0d;
            foreach (var edge in edges)
            {
                var floor = lower;
                charts.CloseTimes.Add(new CountRow { Name = edge.Name, Count = hours.Count(h => h >= floor && h < edge.UpToHours) });
                lower = edge.UpToHours;
            }

            if (engineers)
            {
                charts.Engineers = rows
                    .Where(r => !r.IsClosed && !string.IsNullOrWhiteSpace(r.AssignedTo))
                    .GroupBy(r => r.AssignedTo!.Trim())
                    .Select(group => new CountRow { Name = group.Key, Count = group.Count() })
                    .OrderByDescending(row => row.Count).ThenBy(row => row.Name)
                    .Take(10)
                    .ToList();
            }

            return charts;
        }

        /// <summary>The chart numbers as JSON (camelCase), safe to place inside a script element of a page.</summary>
        public static string ToJson(TicketCharts charts)
        {
            return JsonSerializer.Serialize(charts, JsonOptions);
        }

        private static double Hours(Row row)
        {
            return Math.Max(0, (row.ClosedAt - row.Raised).TotalHours);
        }

        private static double? Median(List<Row> closed)
        {
            if (closed.Count == 0) { return null; }
            var hours = closed.Select(Hours).OrderBy(h => h).ToList();
            var middle = hours.Count / 2;
            return Math.Round(hours.Count % 2 == 1 ? hours[middle] : (hours[middle - 1] + hours[middle]) / 2, 1);
        }

        /// <summary>Rows per key, biggest first; what does not fit into <paramref name="top"/> rows is added up in one last row.</summary>
        private static List<SplitRow> Split(List<Row> rows, Func<Row, int?> key, Func<int?, string> name, int top, string rest)
        {
            var all = rows
                .GroupBy(key)
                .Select(group => new SplitRow { Name = name(group.Key), Open = group.Count(r => !r.IsClosed), Closed = group.Count(r => r.IsClosed) })
                .OrderByDescending(row => row.Open + row.Closed).ThenBy(row => row.Name)
                .ToList();
            if (all.Count <= top + 1)
            {
                return all;
            }

            var shown = all.Take(top).ToList();
            shown.Add(new SplitRow { Name = rest, Open = all.Skip(top).Sum(row => row.Open), Closed = all.Skip(top).Sum(row => row.Closed) });
            return shown;
        }

        private static IEnumerable<(DateTime Start, DateTime Stop)> Buckets(DateTime from, DateTime end, string bucket)
        {
            if (bucket == "month")
            {
                // calendar months; the first and the last one are cut to the period
                var start = from;
                while (start < end)
                {
                    var next = new DateTime(start.Year, start.Month, 1).AddMonths(1);
                    yield return (start, next < end ? next : end);
                    start = next;
                }

                yield break;
            }

            var step = bucket == "week" ? 7 : 1;
            // weeks are counted back from the last day, so the newest point is always a full week
            var first = bucket == "week" ? end.AddDays(-step * (int)Math.Ceiling((end - from).TotalDays / step)) : from;
            for (var start = first; start < end; start = start.AddDays(step))
            {
                yield return (start < from ? from : start, start.AddDays(step));
            }
        }
    }
}
