using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Newtonsoft.Json;
using XFLCSMS.Infrastructure;
using XFLCSMS.Models.Admin;
using XFLCSMS.Models.Affected;
using XFLCSMS.Models.Branch;
using XFLCSMS.Models.Brocarage;
using XFLCSMS.Models.Common;
using XFLCSMS.Models.Issue;
using XFLCSMS.Models.Register;
using XFLCSMS.Models.Support;
using XFLCSMS.Models.Todos;
using XFLCSMS.Services;

namespace XFLCSMS.Controllers
{
    // Ticket pages that are the same for every role: the lists, the board, setting a status, deleting a ticket.
    public abstract partial class CsmsController
    {
        // ---- lists ----------------------------------------------------------------------------------

        /// <summary>
        /// One ticket list: the tickets this role may see, narrowed by <paramref name="scope"/> (unassigned, closed,
        /// assigned to me, ...) and by the status chosen above the list, then searched, sorted and cut into pages.
        /// Every list action of every role ends here, so they cannot drift apart.
        /// </summary>
        protected async Task<IActionResult> TicketListPage(Func<IQueryable<IssueTable>, IQueryable<IssueTable>> scope,
            int page, int rowperpage, string? searchString, string? sortField, bool sortAscending, string? status = null, bool byStatus = true)
        {
            try
            {
                if (page <= 0) { page = 1; }
                ViewBag.CurrentSortField = sortField;
                ViewBag.CurrentSortAscending = sortAscending;

                var tickets = await scope(VisibleIssues).OrderByDescending(i => i.IssueId).ToListAsync();

                if (byStatus)
                {
                    // the numbers on the status chips count the whole list, not the page
                    var counts = TicketStatus.Keys.ToDictionary(key => key, key => 0);
                    foreach (var ticket in tickets)
                    {
                        var key = TicketStatus.Normalize(ticket.IStatus);
                        if (key != null) { counts[key]++; }
                    }

                    ViewBag.StatusCounts = counts;
                    ViewBag.StatusTotal = tickets.Count;

                    var chosen = TicketStatus.Normalize(status);
                    ViewBag.StatusFilter = chosen;
                    if (chosen != null)
                    {
                        tickets = tickets.Where(ticket => TicketStatus.Normalize(ticket.IStatus) == chosen).ToList();
                    }
                }

                // short name and name of the products, for the search and for the line under the title
                var products = await Db.Products.ToDictionaryAsync(product => product.ProductId);
                ViewBag.ProductLabels = products.ToDictionary(pair => pair.Key, pair => pair.Value.ShortName);

                if (!string.IsNullOrWhiteSpace(searchString))
                {
                    var text = searchString.Trim().ToLower();
                    tickets = tickets.Where(e =>
                        e.TNumber.ToLower().Contains(text) ||
                        (e.ProductId != null && products.TryGetValue(e.ProductId.Value, out var product)
                            && (product.Name.ToLower().Contains(text) || product.Code?.ToLower().Contains(text) == true)) ||
                        e.ITitle?.ToLower().Contains(text) == true ||
                        e.Priority?.ToLower().Contains(text) == true ||
                        TicketStatus.Name(e.IStatus).ToLower().Contains(text) ||   // the name people see, not the stored key
                        e.AssignOn?.ToString().ToLower().Contains(text) == true ||
                        e.TDate.ToString().ToLower().Contains(text) ||
                        e.AssignBy?.ToLower().Contains(text) == true).ToList();
                }

                switch (sortField)
                {
                    case "TNumber":
                        tickets = (sortAscending ? tickets.OrderBy(item => item.TNumber) : tickets.OrderByDescending(item => item.TNumber)).ToList();
                        break;
                    case "Tickets":
                        tickets = (sortAscending ? tickets.OrderBy(item => item.ITitle) : tickets.OrderByDescending(item => item.ITitle)).ToList();
                        break;
                    case "Approval Status":
                        tickets = (sortAscending ? tickets.OrderBy(item => item.AssignBy) : tickets.OrderByDescending(item => item.AssignBy)).ToList();
                        break;
                    case "Priority":
                        // Low, Medium, High - not the alphabet
                        tickets = (sortAscending
                            ? tickets.OrderBy(item => Array.IndexOf(TicketService.Priorities, item.Priority))
                            : tickets.OrderByDescending(item => Array.IndexOf(TicketService.Priorities, item.Priority))).ToList();
                        break;
                    case "Status":
                        // in the order of a ticket's life - not the alphabet
                        tickets = (sortAscending
                            ? tickets.OrderBy(item => Array.IndexOf(TicketStatus.Keys, TicketStatus.Normalize(item.IStatus)))
                            : tickets.OrderByDescending(item => Array.IndexOf(TicketStatus.Keys, TicketStatus.Normalize(item.IStatus)))).ToList();
                        break;
                }

                var pageSize = rowperpage > 0 ? Math.Min(rowperpage, 200) : 10;
                var pager = new Pager(tickets.Count, page, pageSize, 4, searchString);
                ViewBag.pager = pager;
                var shown = tickets.Skip((Math.Max(pager.CurrentPage, 1) - 1) * pageSize).Take(pageSize).ToList();
                SetRaisers(shown);
                return View(shown);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        /// <summary>Tickets that wait for an engineer.</summary>
        public Task<IActionResult> UnassignedTicketList(int page, int rowperpage, string? searchString = null, string? sortField = null, bool sortAscending = true)
        {
            return TicketListPage(tickets => tickets.Where(TicketService.Unassigned).Where(TicketService.NotClosed),
                page, rowperpage, searchString, sortField, sortAscending, byStatus: false);
        }

        public Task<IActionResult> ClosedTicketList(int page, int rowperpage, string? searchString = null, string? sortField = null, bool sortAscending = true)
        {
            return TicketListPage(tickets => tickets.Where(i => i.IStatus == TicketStatus.Closed),
                page, rowperpage, searchString, sortField, sortAscending, byStatus: false);
        }

        /// <summary>The tickets this role may see as a board: one column per status.</summary>
        [HttpGet]
        public async Task<IActionResult> TicketBoard(bool mine = false)
        {
            try
            {
                const int perColumn = 12;
                var me = CurrentUser!;
                var source = VisibleIssues;
                var canFilterMine = MyRole == Role.SupportEngineer;
                if (mine && canFilterMine)
                {
                    source = source.Where(TicketService.AssignedTo(me));
                }

                // closed tickets: only the recent ones, the board is about work in hand
                var since = DateTime.Now.AddDays(-30);
                var rows = await source
                    .Where(i => i.IStatus != TicketStatus.Closed || i.ClosedOn >= since)
                    .OrderByDescending(i => i.UpdatedOn ?? i.TDate)
                    .ToListAsync();

                var board = new TicketBoardView { Mine = mine && canFilterMine, CanFilterMine = canFilterMine };
                foreach (var status in TicketStatus.All)
                {
                    var column = rows.Where(row => TicketStatus.Normalize(row.IStatus) == status.Key)
                        .OrderByDescending(row => Array.IndexOf(TicketService.Priorities, row.Priority))
                        .ThenBy(row => row.TDate)
                        .ToList();
                    board.Columns.Add(new TicketBoardColumn { Status = status, Total = column.Count, Tickets = column.Take(perColumn).ToList() });
                }

                var houseIds = board.Columns.SelectMany(column => column.Tickets).Select(ticket => ticket.BrokerageId).Distinct().ToList();
                ViewBag.Houses = await Db.Brokerages.Where(b => houseIds.Contains(b.BrokerageId)).ToDictionaryAsync(b => b.BrokerageId, b => b.BrokerageHouseAcronym);
                return View(board);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        // ---- report ---------------------------------------------------------------------------------

        [HttpGet]
        public async Task<IActionResult> Reports()
        {
            try
            {
                return View(new ReportView
                {
                    // everybody who has closed a ticket this role can see (engineers, managers, people who have left)
                    EmployeeNames = await VisibleIssues.Where(i => i.ClosedBy != null && i.ClosedBy != "").Select(i => i.ClosedBy!).Distinct().OrderBy(name => name).ToListAsync(),
                    brocarages = Rbac.IsStaff(MyRole)
                        ? await Db.Brokerages.OrderBy(house => house.BrokerageHouseName).ToListAsync()
                        : new List<Brokerage>(),
                    Products = await Db.Products.OrderBy(product => product.Name).ToListAsync()
                });
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        /// <summary>
        /// Runs the ticket report over the tickets this role may see. The form posts its filters under a name that
        /// differs per role (search, SESearch, MakerSearch - as the pages always did); all three are understood.
        /// </summary>
        /// <param name="productId">Only the tickets of this product (the same field name for every role).</param>
        [HttpGet]
        public async Task<IActionResult> Search(ReportView reportView, int? productId = null)
        {
            try
            {
                var me = CurrentUser!;
                var staff = Rbac.IsStaff(MyRole);
                var filter = reportView.search
                    ?? (reportView.SESearch != null
                        ? new Search { BrokerageId = reportView.SESearch.BrokerageId, Priority = reportView.SESearch.Priority, AStatus = reportView.SESearch.AStatus, FromDate = reportView.SESearch.FromDate, ToDate = reportView.SESearch.ToDate }
                        : reportView.MakerSearch != null
                            ? new Search { Priority = reportView.MakerSearch.Priority, AStatus = reportView.MakerSearch.AStatus, FromDate = reportView.MakerSearch.FromDate, ToDate = reportView.MakerSearch.ToDate }
                            : new Search());

                var query = VisibleIssues;
                if (MyRole == Role.SupportEngineer)
                {
                    // an engineer reports on the own work: what is assigned to him and what he closed
                    var myName = me.FullName;
                    var myId = me.Id;
                    query = query.Where(x => x.ClosedBy == myName || x.AssignedToId == myId || (x.AssignedToId == null && x.AssignBy == myName));
                }

                if (staff && filter.BrokerageId > 0)
                {
                    query = query.Where(x => x.BrokerageId == filter.BrokerageId);
                }

                if (!string.IsNullOrEmpty(filter.Priority))
                {
                    query = query.Where(x => x.Priority == filter.Priority);
                }

                var products = await Db.Products.ToListAsync();
                var product = productId > 0 ? products.FirstOrDefault(item => item.ProductId == productId) : null;
                if (productId > 0)
                {
                    query = query.Where(x => x.ProductId == productId);
                }

                var closedBy = staff && MyRole != Role.SupportEngineer && !string.IsNullOrEmpty(filter.EmployeeName) ? filter.EmployeeName : null;
                if (closedBy != null)
                {
                    query = query.Where(x => x.ClosedBy == closedBy);
                }

                // both ends of the range are inclusive, and either end may be left empty
                if (filter.FromDate != null)
                {
                    var from = filter.FromDate.Value.Date;
                    query = query.Where(x => x.TDate >= from);
                }

                if (filter.ToDate != null)
                {
                    var until = filter.ToDate.Value.Date.AddDays(1);
                    query = query.Where(x => x.TDate < until);
                }

                var results = await query.OrderByDescending(x => x.IssueId).ToListAsync();
                var status = TicketStatus.Normalize(filter.AStatus);
                if (status != null)
                {
                    results = results.Where(x => TicketStatus.Normalize(x.IStatus) == status).ToList();
                }

                var byStatus = TicketStatus.Keys.ToDictionary(key => key, key => results.Count(x => TicketStatus.Normalize(x.IStatus) == key));
                var header = new HeaderInfo
                {
                    BrokerageHouseName = staff
                        ? (filter.BrokerageId > 0 ? HouseName(filter.BrokerageId) ?? "All" : "All")
                        : HouseName(me.BrokerageHouseName),
                    EmployeeName = MyRole == Role.SupportEngineer ? me.FullName
                        : staff ? closedBy ?? "All"
                        : Can(Permission.TicketsHouse) ? "Everybody of the house" : me.FullName,
                    TotalTicket = results.Count,
                    TotalOpenTicket = byStatus[TicketStatus.Unassigned],
                    TotalCloseTicket = byStatus[TicketStatus.Closed],
                    TotalInque = results.Count - byStatus[TicketStatus.Unassigned] - byStatus[TicketStatus.Closed],
                    ByStatus = byStatus,
                    StatusName = status == null ? null : TicketStatus.Name(status),
                    ProductName = product?.Name,
                    ReportName = Rbac.Label(MyRole)
                };

                // the charts of the report: the same tickets, from the first to the last day the report covers
                TicketCharts? charts = null;
                if (results.Count > 0)
                {
                    var rows = results.Select(x => new TicketStats.Row
                    {
                        IssueId = x.IssueId, BrokerageId = x.BrokerageId, ProductId = x.ProductId, Status = TicketStatus.Normalize(x.IStatus) ?? x.IStatus,
                        Priority = x.Priority, Raised = x.TDate, ClosedOn = x.ClosedOn, UpdatedOn = x.UpdatedOn, AssignedTo = x.AssignBy
                    }).ToList();
                    var first = filter.FromDate?.Date ?? rows.Min(row => row.Raised).Date;
                    var last = filter.ToDate?.Date ?? DateTime.Now.Date;
                    if (last < first) { last = rows.Max(row => row.Raised).Date; }
                    var houses = staff && Can(Permission.TicketsAll) && !(filter.BrokerageId > 0)
                        ? await Db.Brokerages.ToDictionaryAsync(house => house.BrokerageId, house => house.BrokerageHouseName)
                        : null;
                    charts = TicketStats.Build(rows, first, last, compare: false, products.ToDictionary(item => item.ProductId, item => item.Name), houses, engineers: false, closedWhenever: true);
                    charts.PeriodName = first.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture) + " \u2013 " + last.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
                }

                return PartialView("_SearchResults", new ReportView { Issues = results, HeaderInfo = header, Products = products, Charts = charts });
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        // ---- one ticket --------------------------------------------------------------------------------

        public async Task<IActionResult> TicketView(int? id)
        {
            try
            {
                var issue = await Db.Issues.Include(i => i.attachment).FirstOrDefaultAsync(i => i.IssueId == id);
                if (issue == null || !CanAccessIssue(issue))
                {
                    return NotFound();
                }

                return View(ToMakerView(issue, includeEngineers: false));
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        public async Task<IActionResult> EditTicket(int id)
        {
            try
            {
                var issue = await Db.Issues.Include(i => i.attachment).FirstOrDefaultAsync(i => i.IssueId == id);
                if (issue == null || !CanAccessIssue(issue))
                {
                    return NotFound();
                }

                var refusal = RefuseEdit(issue);
                if (refusal != null)
                {
                    return refusal;
                }

                return View(ToMakerView(issue, includeEngineers: Rbac.IsStaff(MyRole)));
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        /// <summary>Saves the edit form. XFL staff: text, status, engineer. People of the house: text and priority.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditTicketttt(MakerView makerView, List<IFormFile> files)
        {
            try
            {
                var editor = CurrentUser!;
                var issue = await Db.Issues.FirstOrDefaultAsync(a => a.IssueId == makerView.IssueId);
                if (issue == null || !CanAccessIssue(issue))
                {
                    return NotFound("The ticket was not found.");
                }

                var refusal = RefuseEdit(issue);
                if (refusal != null)
                {
                    return refusal;
                }

                var problems = new List<string>();
                if (Rbac.IsStaff(MyRole))
                {
                    problems.AddRange(Tickets.ApplyStaffEdit(issue, makerView, editor, canApprove: true));
                }
                else
                {
                    Tickets.ApplyOwnerEdit(issue, makerView, editor);
                }

                await Db.SaveChangesAsync();

                var rejected = await Tickets.SaveAttachmentsAsync(issue.IssueId, files);
                if (rejected.Count > 0)
                {
                    problems.Add("these files were not attached (file type not allowed): " + string.Join(", ", rejected));
                }

                if (problems.Count > 0)
                {
                    TempData["ErrorMessage"] = "The ticket was saved, but " + string.Join(" Also, ", problems);
                }
                else
                {
                    TempData["SuccessMessage"] = "Ticket " + issue.TNumber + " was saved.";
                }

                return RedirectToAction("TicketView", new { id = issue.IssueId });
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        /// <summary>The status buttons on the ticket page and the board: one step, no form to fill in.</summary>
        [HttpPost]
        public async Task<IActionResult> SetTicketStatus(int id, string status, string? returnTo = null)
        {
            try
            {
                var issue = await Db.Issues.FirstOrDefaultAsync(i => i.IssueId == id);
                if (issue == null || !CanAccessIssue(issue))
                {
                    return NotFound();
                }

                if (!Tickets.CanWorkOn(issue))
                {
                    return Refused();
                }

                var wanted = TicketStatus.Normalize(status);
                if (wanted == null)
                {
                    TempData["ErrorMessage"] = "Choose a status.";
                }
                else
                {
                    var refusal = Tickets.SetStatus(issue, wanted);
                    if (refusal != null)
                    {
                        TempData["ErrorMessage"] = "Ticket " + issue.TNumber + ": " + refusal;
                    }
                    else
                    {
                        await Db.SaveChangesAsync();
                        TempData["SuccessMessage"] = "Ticket " + issue.TNumber + " is " + TicketStatus.Name(issue.IStatus).ToLowerInvariant() + " now.";
                    }
                }

                return string.Equals(returnTo, "TicketBoard", StringComparison.OrdinalIgnoreCase)
                    ? RedirectToAction("TicketBoard")
                    : BackTo(returnTo, id);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [HttpDelete]
        [RequirePermission(Permission.TicketDelete)]
        public async Task<IActionResult> DeleteTicket(int id)
        {
            try
            {
                var issue = await Db.Issues.Include(i => i.attachment).FirstOrDefaultAsync(i => i.IssueId == id);
                if (issue == null || !CanAccessIssue(issue))
                {
                    return NotFound("The ticket was not found.");
                }

                // remove the uploaded files too, not only the database rows
                var files = issue.attachment?.ToList() ?? new List<Attachment>();
                foreach (var attachment in files)
                {
                    await Tickets.DeleteAttachmentAsync(attachment, quiet: true);
                }

                Tickets.Log(AuditActions.TicketDelete, issue, "Deleted the ticket \u201c" + issue.ITitle + "\u201d"
                    + (files.Count == 0 ? string.Empty : " with " + files.Count + (files.Count == 1 ? " file" : " files")));
                Db.Issues.Remove(issue);
                await Db.SaveChangesAsync();
                return Ok();
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }
    }
}
