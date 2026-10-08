using Microsoft.AspNetCore.Mvc;
using XFLCSMS.Infrastructure;
using XFLCSMS.Models.Admin;
using XFLCSMS.Services;

namespace XFLCSMS.Controllers
{
    // The numbers behind the charts (dashboard, ticket report). Always over the tickets this role may see.
    public abstract partial class CsmsController
    {
        /// <summary>What the charts need of the tickets in <paramref name="tickets"/>.</summary>
        protected static IQueryable<TicketStats.Row> ChartRows(IQueryable<Models.Issue.IssueTable> tickets)
        {
            return tickets.Select(i => new TicketStats.Row
            {
                IssueId = i.IssueId, BrokerageId = i.BrokerageId, ProductId = i.ProductId, Status = i.IStatus, Priority = i.Priority,
                Raised = i.TDate, ClosedOn = i.ClosedOn, UpdatedOn = i.UpdatedOn, AssignedTo = i.AssignBy
            });
        }

        /// <summary>Chart numbers of the dashboard for the last <paramref name="days"/> days (7, 30, 90 or 365).</summary>
        protected async Task<TicketCharts> BuildChartsAsync(int days)
        {
            days = TicketStats.CleanDays(days);
            var today = DateTime.Now.Date;
            var rows = await ChartRows(VisibleIssues).ToListAsync();
            foreach (var row in rows)
            {
                row.Status = TicketStatus.Normalize(row.Status) ?? row.Status;
            }

            var products = await Db.Products.ToDictionaryAsync(product => product.ProductId, product => product.Name);
            // a chart per brokerage house only for who sees the tickets of every house
            var houses = Can(Permission.TicketsAll)
                ? await Db.Brokerages.ToDictionaryAsync(house => house.BrokerageId, house => house.BrokerageHouseName)
                : null;

            var charts = TicketStats.Build(rows, TicketStats.PeriodStart(days, today), today, compare: true, products, houses, engineers: Can(Permission.Workload));
            charts.Days = days;
            charts.PeriodName = TicketStats.PeriodName(days);
            return charts;
        }

        /// <summary>The chart numbers of the dashboard as JSON: the page asks for them when another period is chosen.</summary>
        [HttpGet]
        public async Task<IActionResult> DashboardData(int days = 30)
        {
            try
            {
                Response.Headers["Cache-Control"] = "no-store";
                return Content(TicketStats.ToJson(await BuildChartsAsync(days)), "application/json");
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }
    }
}
