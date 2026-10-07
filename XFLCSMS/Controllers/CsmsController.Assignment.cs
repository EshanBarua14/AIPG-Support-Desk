using Microsoft.AspNetCore.Mvc;
using XFLCSMS.Infrastructure;
using XFLCSMS.Models.Admin;
using XFLCSMS.Models.Issue;
using XFLCSMS.Services;

namespace XFLCSMS.Controllers
{
    // Ticket assignment. Who may do what is in Infrastructure/Rbac.cs:
    //   TicketAssign (platform admin, support manager)  give any open ticket to any engineer, or to nobody
    //   TicketTake   (support engineer)                 take an unassigned ticket, give an own ticket back
    //   Workload     (platform admin, support manager)  how many tickets each engineer has
    public abstract partial class CsmsController
    {
        /// <summary>Lists a ticket may return to after an assignment made from that list.</summary>
        private static readonly string[] ListActions =
        {
            "AdminView", "AllTicketList", "AssignedTicketList", "UnassignedTicketList", "Workload", "TicketBoard"
        };

        private IActionResult BackTo(string? returnTo, int ticketId)
        {
            var list = ListActions.FirstOrDefault(action => string.Equals(action, returnTo, StringComparison.OrdinalIgnoreCase));
            return list != null ? RedirectToAction(list) : RedirectToAction("TicketView", new { id = ticketId });
        }

        /// <summary>Engineers for the "Assigned to" choices, with the number of open tickets each one has.</summary>
        protected List<EngineerLoad> LoadEngineerChoices()
        {
            var engineers = Tickets.Engineers().OrderBy(u => u.FullName).Select(u => new { u.Id, u.FullName }).ToList();
            var open = Db.Issues.Where(i => i.IStatus != TicketStatus.Closed && (i.AssignedToId != null || i.AssignBy != null))
                .Select(i => new { i.AssignedToId, i.AssignBy })
                .ToList();

            return engineers
                .Select(e => new EngineerLoad
                {
                    Id = e.Id,
                    Name = e.FullName,
                    Open = open.Count(t => t.AssignedToId == e.Id || (t.AssignedToId == null && t.AssignBy == e.FullName))
                })
                .ToList();
        }

        [HttpPost]
        [RequirePermission(Permission.TicketAssign)]
        public async Task<IActionResult> AssignTicket(int id, int? engineerId, string? returnTo = null)
        {
            try
            {
                var issue = await Db.Issues.FirstOrDefaultAsync(i => i.IssueId == id);
                if (issue == null || !CanAccessIssue(issue))
                {
                    return NotFound();
                }

                if (TicketStatus.IsClosed(issue.IStatus))
                {
                    TempData["ErrorMessage"] = "Ticket " + issue.TNumber + " is closed. Reopen it before assigning it.";
                    return BackTo(returnTo, id);
                }

                var engineer = engineerId == null ? null : await Tickets.Engineers().FirstOrDefaultAsync(u => u.Id == engineerId);
                if (engineerId != null && engineer == null)
                {
                    TempData["ErrorMessage"] = "Choose a support engineer from the list. Disabled accounts and other roles cannot be assigned.";
                    return BackTo(returnTo, id);
                }

                var done = Tickets.Assign(issue, engineer);
                await Db.SaveChangesAsync();

                if (done != null)
                {
                    TempData["SuccessMessage"] = "Ticket " + issue.TNumber + ": " + char.ToLowerInvariant(done[0]) + done.Substring(1) + ".";
                }

                return BackTo(returnTo, id);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        /// <summary>An engineer takes a ticket nobody has yet.</summary>
        [HttpPost]
        [RequirePermission(Permission.TicketTake)]
        public async Task<IActionResult> TakeTicket(int id, string? returnTo = null)
        {
            try
            {
                var me = CurrentUser!;
                var issue = await Db.Issues.FirstOrDefaultAsync(i => i.IssueId == id);
                if (issue == null || !CanAccessIssue(issue))
                {
                    return NotFound();
                }

                if (TicketStatus.IsClosed(issue.IStatus))
                {
                    TempData["ErrorMessage"] = "Ticket " + issue.TNumber + " is closed.";
                }
                else if (TicketService.IsAssignedTo(issue, me))
                {
                    TempData["SuccessMessage"] = "Ticket " + issue.TNumber + " is already yours.";
                }
                else if (!TicketService.IsUnassigned(issue))
                {
                    TempData["ErrorMessage"] = "Ticket " + issue.TNumber + " is assigned to " + issue.AssignBy + ". A support manager can reassign it.";
                }
                else
                {
                    var self = await Tickets.Engineers().FirstOrDefaultAsync(u => u.Id == me.Id);
                    if (self == null)
                    {
                        return Refused();
                    }

                    Tickets.Assign(issue, self);
                    await Db.SaveChangesAsync();
                    TempData["SuccessMessage"] = "Ticket " + issue.TNumber + " is yours now. You find it under Assigned to me.";
                }

                return BackTo(returnTo, id);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        /// <summary>An engineer gives a ticket back: it is unassigned again.</summary>
        [HttpPost]
        [RequirePermission(Permission.TicketTake)]
        public async Task<IActionResult> ReleaseTicket(int id, string? returnTo = null)
        {
            try
            {
                var me = CurrentUser!;
                var issue = await Db.Issues.FirstOrDefaultAsync(i => i.IssueId == id);
                if (issue == null || !CanAccessIssue(issue))
                {
                    return NotFound();
                }

                if (!TicketService.IsAssignedTo(issue, me))
                {
                    TempData["ErrorMessage"] = "Ticket " + issue.TNumber + " is not assigned to you.";
                }
                else if (TicketStatus.IsClosed(issue.IStatus))
                {
                    TempData["ErrorMessage"] = "Ticket " + issue.TNumber + " is closed.";
                }
                else
                {
                    Tickets.Assign(issue, null);
                    await Db.SaveChangesAsync();
                    TempData["SuccessMessage"] = "Ticket " + issue.TNumber + " is unassigned again.";
                }

                return BackTo(returnTo, id);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        /// <summary>Who has how much: one line per engineer, and what is waiting for an engineer.</summary>
        [HttpGet]
        [RequirePermission(Permission.Workload)]
        public async Task<IActionResult> Workload()
        {
            try
            {
                var today = DateTime.Now.Date;
                var since = today.AddDays(-30);
                var engineers = await Tickets.Engineers().OrderBy(u => u.FullName).Select(u => new { u.Id, u.FullName, u.UserName }).ToListAsync();
                // only what this role may see (staff without "see all tickets": their own)
                var tickets = await VisibleIssues
                    .Select(i => new { i.IssueId, i.AssignedToId, i.AssignBy, i.AssignOn, i.IStatus, i.Priority, i.TDate, i.ClosedOn })
                    .ToListAsync();

                var board = new WorkloadView();
                foreach (var engineer in engineers)
                {
                    var mine = tickets.Where(t => t.AssignedToId == engineer.Id || (t.AssignedToId == null && t.AssignBy == engineer.FullName)).ToList();
                    var open = mine.Where(t => t.IStatus != TicketStatus.Closed).ToList();
                    board.Engineers.Add(new EngineerLoad
                    {
                        Id = engineer.Id,
                        Name = engineer.FullName,
                        UserName = engineer.UserName,
                        Open = open.Count,
                        InProgress = open.Count(t => t.IStatus == TicketStatus.InProgress),
                        Pending = open.Count(t => t.IStatus == TicketStatus.Pending),
                        InReview = open.Count(t => t.IStatus == TicketStatus.Review),
                        ToClose = open.Count(t => t.IStatus == TicketStatus.Done || t.IStatus == TicketStatus.Deployed),
                        HighPriority = open.Count(t => t.Priority == "High"),
                        OldestOpen = open.Count == 0 ? null : open.Min(t => t.AssignOn ?? t.TDate),
                        ClosedLast30Days = mine.Count(t => t.IStatus == TicketStatus.Closed && t.ClosedOn >= since)
                    });
                }

                var waitingIds = tickets
                    .Where(t => t.AssignedToId == null && string.IsNullOrEmpty(t.AssignBy) && t.IStatus != TicketStatus.Closed)
                    .OrderBy(t => t.TDate)
                    .Select(t => t.IssueId)
                    .ToList();
                board.UnassignedCount = waitingIds.Count;
                var firstWaiting = waitingIds.Take(15).ToList();
                var rows = await VisibleIssues.Where(i => firstWaiting.Contains(i.IssueId)).ToListAsync();
                board.Unassigned = firstWaiting.Select(id => rows.First(r => r.IssueId == id)).ToList();

                // open tickets whose engineer can no longer work on them (account disabled, deleted or another role now)
                var names = engineers.Select(e => e.FullName).ToHashSet();
                var ids = engineers.Select(e => e.Id).ToHashSet();
                board.Orphaned = tickets.Count(t => t.IStatus != TicketStatus.Closed
                    && ((t.AssignedToId != null && !ids.Contains(t.AssignedToId.Value)) || (t.AssignedToId == null && !string.IsNullOrEmpty(t.AssignBy) && !names.Contains(t.AssignBy))));

                var houses = await Db.Brokerages.ToDictionaryAsync(b => b.BrokerageId, b => b.BrokerageHouseName);
                ViewBag.Houses = houses;
                return View(board);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }
    }
}
