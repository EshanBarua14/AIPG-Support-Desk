using System.Globalization;
using XFLCSMS.Models.Brocarage;
using XFLCSMS.Models.Desk;
using XFLCSMS.Models.Issue;

namespace XFLCSMS.Services
{
    /// <summary>
    /// Turns tickets into the numbers of the service report. Times are working time (the calendar of the ticket's
    /// priority), so a ticket raised on Thursday evening and answered on Sunday morning counts as answered within
    /// minutes, not days. Time a ticket spent in "Pending" is not part of its time to the solution.
    /// </summary>
    public static class ServiceReportBuilder
    {
        public static ServiceReport Build(List<IssueTable> period, List<IssueTable> openNow, SlaService sla, List<Brokerage> houses, DateTime now, bool forStaff, Dictionary<int, string>? products = null)
        {
            var report = new ServiceReport
            {
                Raised = period.Count,
                Solved = period.Count(ticket => ticket.ResolvedAt != null),
                Reopened = period.Count(ticket => ticket.ReopenCount > 0),
                OpenNow = openNow.Count,
                OverdueNow = openNow.Count(ticket => sla.Response(ticket, now).State == "overdue" || sla.Resolve(ticket, now).State == "overdue"),
                WaitingForResponse = openNow.Count(ticket => ticket.FirstResponseAt == null && !SlaService.IsSolved(ticket.IStatus))
            };

            Fill(report, period, sla, now);

            var rated = period.Where(ticket => ticket.Rating != null).ToList();
            foreach (var ticket in rated)
            {
                report.RatingCounts[Math.Clamp(ticket.Rating!.Value, 1, 5) - 1]++;
            }

            report.Backlog = new List<(string, int)>
            {
                ("Less than a day", openNow.Count(ticket => now - ticket.TDate < TimeSpan.FromDays(1))),
                ("1 to 3 days", openNow.Count(ticket => now - ticket.TDate >= TimeSpan.FromDays(1) && now - ticket.TDate < TimeSpan.FromDays(3))),
                ("3 to 7 days", openNow.Count(ticket => now - ticket.TDate >= TimeSpan.FromDays(3) && now - ticket.TDate < TimeSpan.FromDays(7))),
                ("More than a week", openNow.Count(ticket => now - ticket.TDate >= TimeSpan.FromDays(7)))
            };

            foreach (var priority in new[] { "High", "Medium", "Low" })
            {
                var row = RowOf(priority, period.Where(ticket => ticket.Priority == priority).ToList(), sla, now);
                row.OpenNow = openNow.Count(ticket => ticket.Priority == priority);
                report.ByPriority.Add(row);
            }

            // per product: every role may see this, a product says nothing about another house
            if (products != null && products.Count > 0)
            {
                int? Named(int? id) => id != null && products.ContainsKey(id.Value) ? id : null;
                report.ByProduct = period.GroupBy(ticket => Named(ticket.ProductId))
                    .Select(group =>
                    {
                        var row = RowOf(group.Key == null ? "No product" : products[group.Key.Value], group.ToList(), sla, now);
                        row.OpenNow = openNow.Count(ticket => Named(ticket.ProductId) == group.Key);
                        return (Row: row, HasName: group.Key != null);
                    })
                    .OrderByDescending(item => item.HasName).ThenByDescending(item => item.Row.Raised).ThenBy(item => item.Row.Name)
                    .Select(item => item.Row).ToList();
            }

            if (forStaff)
            {
                report.ByEngineer = period.Where(ticket => !string.IsNullOrEmpty(ticket.AssignBy))
                    .GroupBy(ticket => ticket.AssignBy!)
                    .Select(group =>
                    {
                        var row = RowOf(group.Key, group.ToList(), sla, now);
                        row.OpenNow = openNow.Count(ticket => ticket.AssignBy == group.Key);
                        return row;
                    })
                    .OrderByDescending(row => row.Raised).ThenBy(row => row.Name).ToList();

                var names = houses.ToDictionary(house => house.BrokerageId, house => house.BrokerageHouseName);
                report.ByHouse = period.GroupBy(ticket => ticket.BrokerageId)
                    .Select(group =>
                    {
                        var row = RowOf(names.GetValueOrDefault(group.Key) ?? "House " + group.Key, group.ToList(), sla, now);
                        row.OpenNow = openNow.Count(ticket => ticket.BrokerageId == group.Key);
                        return row;
                    })
                    .OrderByDescending(row => row.Raised).ThenBy(row => row.Name).ToList();
            }

            return report;
        }

        /// <summary>Raised and solved per day (up to 31 days) or per week, oldest first, for the chart.</summary>
        public static void AddTrend(ServiceReport report, List<IssueTable> period, List<IssueTable> solvedInPeriod, DateTime since, DateTime today)
        {
            var days = (today - since).Days + 1;
            report.TrendIsWeekly = days > 31;
            if (!report.TrendIsWeekly)
            {
                for (var day = since; day <= today; day = day.AddDays(1))
                {
                    report.Trend.Add((day.ToString("d MMM", CultureInfo.InvariantCulture),
                        period.Count(ticket => ticket.TDate.Date == day), solvedInPeriod.Count(ticket => ticket.ResolvedAt!.Value.Date == day)));
                }

                return;
            }

            for (var start = since; start <= today; start = start.AddDays(7))
            {
                var end = start.AddDays(7);
                report.Trend.Add((start.ToString("d MMM", CultureInfo.InvariantCulture),
                    period.Count(ticket => ticket.TDate >= start && ticket.TDate < end), solvedInPeriod.Count(ticket => ticket.ResolvedAt >= start && ticket.ResolvedAt < end)));
            }
        }

        private static ServiceReport.Row RowOf(string name, List<IssueTable> tickets, SlaService sla, DateTime now)
        {
            var holder = new ServiceReport();
            Fill(holder, tickets, sla, now);
            return new ServiceReport.Row
            {
                Name = name,
                Raised = tickets.Count,
                Solved = tickets.Count(ticket => ticket.ResolvedAt != null),
                MedianResponse = holder.MedianResponse,
                MedianResolve = holder.MedianResolve,
                ResponseMet = holder.ResponseMet,
                ResolveMet = holder.ResolveMet,
                AverageRating = holder.AverageRating,
                Ratings = holder.Ratings
            };
        }

        private static void Fill(ServiceReport report, List<IssueTable> tickets, SlaService sla, DateTime now)
        {
            var responses = new List<double>();
            var solutions = new List<double>();
            foreach (var ticket in tickets)
            {
                var calendar = sla.Policy.CalendarFor(ticket.Priority);
                if (ticket.FirstResponseAt != null)
                {
                    responses.Add(calendar.MinutesBetween(ticket.TDate, ticket.FirstResponseAt.Value));
                }

                if (ticket.ResolvedAt != null)
                {
                    solutions.Add(Math.Max(0, calendar.MinutesBetween(ticket.TDate, ticket.ResolvedAt.Value) - ticket.SlaPausedMinutes));
                }

                // a target counts once it is decided: reached (in time or late), or overdue and still open
                Count(report.ResponseMet, sla.Response(ticket, now).State);
                Count(report.ResolveMet, sla.Resolve(ticket, now).State);
            }

            report.MedianResponse = Median(responses);
            report.MedianResolve = Median(solutions);
            var rated = tickets.Where(ticket => ticket.Rating != null).Select(ticket => (double)ticket.Rating!.Value).ToList();
            report.Ratings = rated.Count;
            report.AverageRating = rated.Count == 0 ? null : Math.Round(rated.Average(), 1);
        }

        private static void Count(ServiceReport.Share share, string state)
        {
            if (state == "met") { share.Met++; share.Of++; }
            else if (state == "missed" || state == "overdue") { share.Of++; }
        }

        private static double? Median(List<double> values)
        {
            if (values.Count == 0)
            {
                return null;
            }

            values.Sort();
            var middle = values.Count / 2;
            return values.Count % 2 == 1 ? values[middle] : (values[middle - 1] + values[middle]) / 2.0;
        }
    }
}
