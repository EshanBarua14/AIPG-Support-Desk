using System.Text;
using Microsoft.AspNetCore.Mvc;
using XFLCSMS.Infrastructure;
using XFLCSMS.Models.Audit;
using XFLCSMS.Services;

namespace XFLCSMS.Controllers
{
    // The audit trail page. What a role reads:
    //   AuditAll     (platform admin)    every line
    //   AuditTickets (support manager)   the lines about tickets, all brokerage houses
    //   AuditHouse   (house admin)       the lines of the own brokerage house (its tickets, accounts, branches, sign-ins)
    // Everybody who can open a ticket sees the history of that ticket on the ticket page (ToMakerView).
    public abstract partial class CsmsController
    {
        /// <summary>The lines this role may read.</summary>
        protected IQueryable<AuditLog> VisibleAudit
        {
            get
            {
                if (Can(Permission.AuditAll))
                {
                    return Db.AuditLogs;
                }

                if (Can(Permission.AuditTickets))
                {
                    return Db.AuditLogs.Where(line => line.EntityType == "Ticket");
                }

                if (Can(Permission.AuditHouse))
                {
                    var myHouse = CurrentUser?.BrokerageHouseName ?? 0;
                    return Db.AuditLogs.Where(line => line.BrokerageId == myHouse);
                }

                return Db.AuditLogs.Where(line => false);
            }
        }

        [HttpGet]
        [RequirePermission(Permission.AuditAll, Permission.AuditTickets, Permission.AuditHouse)]
        public async Task<IActionResult> AuditTrail(DateTime? from, DateTime? to, string? category, string? q, int? house, int page = 1, string? format = null)
        {
            try
            {
                var all = Can(Permission.AuditAll);
                var lines = VisibleAudit;

                if (from != null)
                {
                    var start = from.Value.Date;
                    lines = lines.Where(line => line.At >= start);
                }

                if (to != null)
                {
                    var end = to.Value.Date.AddDays(1);
                    lines = lines.Where(line => line.At < end);
                }

                category = AuditActions.Categories.Select(c => c.Key).FirstOrDefault(key => key == category);
                if (category != null)
                {
                    var prefix = category + ".";
                    lines = lines.Where(line => line.Action.StartsWith(prefix));
                }

                q = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
                if (q != null)
                {
                    lines = lines.Where(line => line.UserName.Contains(q) || (line.EntityLabel != null && line.EntityLabel.Contains(q)) || (line.Details != null && line.Details.Contains(q)));
                }

                if (all && house != null)
                {
                    lines = lines.Where(line => line.BrokerageId == house);
                }
                else
                {
                    house = null;
                }

                var houses = await Db.Brokerages.ToDictionaryAsync(b => b.BrokerageId, b => b.BrokerageHouseName);

                if (format == "csv")
                {
                    var rows = await lines.OrderByDescending(line => line.Id).Take(10000).ToListAsync();
                    return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(AuditCsv(rows, houses, all))).ToArray(),
                        "text/csv; charset=utf-8", "audit-trail-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".csv");
                }

                const int size = 50;
                var total = await lines.CountAsync();
                var pages = Math.Max(1, (int)Math.Ceiling(total / (double)size));
                page = Math.Min(Math.Max(page, 1), pages);

                var model = new AuditPage
                {
                    Lines = await lines.OrderByDescending(line => line.Id).Skip((page - 1) * size).Take(size).ToListAsync(),
                    Total = total,
                    Page = page,
                    Size = size,
                    From = from,
                    To = to,
                    Category = category,
                    Q = q,
                    House = house,
                    CanFilterHouse = all,
                    Houses = houses,
                    Reach = all ? "Everything that was done in the system, newest first."
                        : Can(Permission.AuditTickets) ? "Everything that was done to tickets, in every brokerage house, newest first."
                        : "Everything that was done in your brokerage house: its tickets, accounts, branches and sign-ins, newest first.",
                    Categories = AuditActions.Categories
                        .Where(c => all || c.Key == "ticket" || (Can(Permission.AuditHouse) && c.Key != "system"))
                        .ToList()
                };

                return View(model);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        private static string AuditCsv(IEnumerable<AuditLog> rows, Dictionary<int, string> houses, bool withAddress)
        {
            static string Cell(string? text)
            {
                text ??= string.Empty;
                // a cell that starts like a formula would be run by a spreadsheet program
                if (text.Length > 0 && "=+-@\t\r".IndexOf(text[0]) >= 0) { text = "'" + text; }
                return "\"" + text.Replace("\"", "\"\"") + "\"";
            }

            var csv = new StringBuilder();
            csv.Append("When,Who,Role,What,Item,Details,Brokerage house" + (withAddress ? ",Address" : string.Empty)).Append("\r\n");
            foreach (var row in rows)
            {
                csv.Append(Cell(row.At.ToString("yyyy-MM-dd HH:mm:ss"))).Append(',')
                    .Append(Cell(row.UserName)).Append(',')
                    .Append(Cell(row.Role)).Append(',')
                    .Append(Cell(AuditActions.Name(row.Action))).Append(',')
                    .Append(Cell(row.EntityLabel)).Append(',')
                    .Append(Cell(row.Details)).Append(',')
                    .Append(Cell(row.BrokerageId != null && houses.TryGetValue(row.BrokerageId.Value, out var name) ? name : string.Empty));
                if (withAddress)
                {
                    csv.Append(',').Append(Cell(row.Ip));
                }

                csv.Append("\r\n"); // the line end of CSV files, on every operating system
            }

            return csv.ToString();
        }
    }
}
