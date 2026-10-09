using Microsoft.AspNetCore.Mvc;
using XFLCSMS.Infrastructure;
using XFLCSMS.Models.Desk;
using XFLCSMS.Models.Issue;
using XFLCSMS.Services;

namespace XFLCSMS.Controllers
{
    // What makes the ticket system a support desk (version 3.3), the same for every role:
    //   the conversation of a ticket (replies, internal notes), rating the support,
    //   canned replies, the service targets, the service report, the list of overdue tickets.
    public abstract partial class CsmsController
    {
        protected SlaService Sla => HttpContext.RequestServices.GetRequiredService<SlaService>();

        // ---- conversation ---------------------------------------------------------------------------------

        /// <summary>Fills the parts of the ticket page that came with version 3.3.</summary>
        private async Task FillDeskAsync(MakerView view, IssueTable issue)
        {
            var now = DateTime.Now;
            view.Messages = await Tickets.MessagesAsync(issue.IssueId);
            view.RaisedById = issue.UserId;
            view.ResponseClock = Sla.Response(issue, now);
            view.ResolveClock = Sla.Resolve(issue, now);
            view.FirstResponseAt = issue.FirstResponseAt;
            view.ResolvedAt = issue.ResolvedAt;
            view.ReopenCount = issue.ReopenCount;
            view.Rating = issue.Rating;
            view.RatingComment = issue.RatingComment;
            view.RatedAt = issue.RatedAt;
            view.CanRate = Tickets.CanRate(issue);
            if (Rbac.IsStaff(MyRole))
            {
                view.CannedReplies = await Db.CannedReplies.Where(reply => reply.IsActive).OrderBy(reply => reply.Title).ToListAsync();
            }

            // the files of the ticket itself stay in "Attachments"; the files of an entry are shown with that entry,
            // and the files of internal notes are not handed to the page at all for people of the house
            var readable = view.Messages.Select(message => (long?)message.Id).ToHashSet();
            view.Attachments = (issue.attachment ?? new List<Attachment>()).Where(file => file.MessageId == null || readable.Contains(file.MessageId)).ToList();
        }

        /// <summary>A reply or an internal note. Everybody who may open the ticket may write in its conversation.</summary>
        [HttpPost]
        public async Task<IActionResult> ReplyTicket(int id, string? body, bool internalNote, List<IFormFile> files)
        {
            try
            {
                var issue = await Db.Issues.FirstOrDefaultAsync(i => i.IssueId == id);
                if (issue == null || !CanAccessIssue(issue))
                {
                    return NotFound();
                }

                var result = await Tickets.AddMessageAsync(issue, body, internalNote, files);
                if (result.Error != null)
                {
                    TempData["ErrorMessage"] = result.Error;
                }
                else if (result.RejectedFiles.Count > 0)
                {
                    TempData["ErrorMessage"] = "Your " + (result.Message!.IsInternal ? "note" : "reply") + " was added, but these files were not attached: " + string.Join(", ", result.RejectedFiles);
                }
                else
                {
                    TempData["SuccessMessage"] = result.Message!.IsInternal ? "Internal note added. Only XFL staff can read it." : "Reply added.";
                }

                return Redirect(Url.Action("TicketView", new { id }) + "#conversation");
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        /// <summary>The person who raised a ticket says how the support was, once, after the ticket was closed.</summary>
        [HttpPost]
        public async Task<IActionResult> RateTicket(int id, int rating, string? comment)
        {
            try
            {
                var issue = await Db.Issues.FirstOrDefaultAsync(i => i.IssueId == id);
                if (issue == null || !CanAccessIssue(issue))
                {
                    return NotFound();
                }

                var refusal = Tickets.Rate(issue, rating, comment);
                if (refusal != null)
                {
                    TempData["ErrorMessage"] = refusal;
                }
                else
                {
                    await Db.SaveChangesAsync();
                    TempData["SuccessMessage"] = "Thank you. Your rating was saved.";
                }

                return RedirectToAction("TicketView", new { id });
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        // ---- overdue ----------------------------------------------------------------------------------------

        /// <summary>Open tickets with a service target that has passed: no first response in time, or not solved in time.</summary>
        public Task<IActionResult> OverdueTicketList(int page, int rowperpage, string? searchString = null, string? sortField = null, bool sortAscending = true)
        {
            var now = DateTime.Now;
            return TicketListPage(tickets => tickets.Where(TicketService.NotClosed).Where(i =>
                    (i.ResponseDueAt != null && i.FirstResponseAt == null && i.ResponseDueAt < now)
                    || (i.ResolveDueAt != null && i.ResolvedAt == null && i.PendingSince == null && i.ResolveDueAt < now)),
                page, rowperpage, searchString, sortField, sortAscending, byStatus: false);
        }

        // ---- canned replies ---------------------------------------------------------------------------------

        [HttpGet]
        [RequirePermission(Permission.CannedReplies)]
        public async Task<IActionResult> CannedReplies(int? edit = null)
        {
            try
            {
                var all = await Db.CannedReplies.OrderBy(reply => reply.Title).ToListAsync();
                ViewBag.Editing = edit == null ? null : all.FirstOrDefault(reply => reply.Id == edit);
                return View(all);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [HttpPost]
        [RequirePermission(Permission.CannedReplies)]
        public async Task<IActionResult> SaveCannedReply(int id, string? title, string? body, bool isActive = false)
        {
            try
            {
                title = (title ?? string.Empty).Trim();
                var clean = HtmlSanitizer.Sanitize(body ?? string.Empty);
                if (title.Length == 0 || title.Length > 120)
                {
                    TempData["ErrorMessage"] = "Give the reply a title of up to 120 characters.";
                    return RedirectToAction("CannedReplies", id > 0 ? new { edit = (int?)id } : null);
                }

                if (TicketService.PlainText(clean).Length == 0)
                {
                    TempData["ErrorMessage"] = "Write the text of the reply.";
                    return RedirectToAction("CannedReplies", id > 0 ? new { edit = (int?)id } : null);
                }

                if (await Db.CannedReplies.AnyAsync(reply => reply.Title == title && reply.Id != id))
                {
                    TempData["ErrorMessage"] = "There is already a reply with the title “" + title + "”.";
                    return RedirectToAction("CannedReplies", id > 0 ? new { edit = (int?)id } : null);
                }

                var me = CurrentUser!;
                CannedReply? reply;
                if (id > 0)
                {
                    reply = await Db.CannedReplies.FirstOrDefaultAsync(item => item.Id == id);
                    if (reply == null)
                    {
                        return NotFound();
                    }
                }
                else
                {
                    reply = new CannedReply();
                    Db.CannedReplies.Add(reply);
                }

                var isNew = id <= 0;
                reply.Title = title;
                reply.Body = clean;
                reply.IsActive = isNew || isActive;
                reply.UpdatedAt = DateTime.Now;
                reply.UpdatedBy = me.FullName;
                Audit(isNew ? AuditActions.DataCreate : AuditActions.DataUpdate, "Canned reply", isNew ? null : reply.Id, title,
                    isNew ? "Added the canned reply" : "Changed the canned reply" + (reply.IsActive ? string.Empty : " (not offered any more)"), null);
                await Db.SaveChangesAsync();

                TempData["SuccessMessage"] = "“" + title + "” was saved.";
                return RedirectToAction("CannedReplies");
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [HttpPost]
        [RequirePermission(Permission.CannedReplies)]
        public async Task<IActionResult> DeleteCannedReply(int id)
        {
            try
            {
                var reply = await Db.CannedReplies.FirstOrDefaultAsync(item => item.Id == id);
                if (reply != null)
                {
                    Audit(AuditActions.DataDelete, "Canned reply", reply.Id, reply.Title, "Removed the canned reply", null);
                    Db.CannedReplies.Remove(reply);
                    await Db.SaveChangesAsync();
                    TempData["SuccessMessage"] = "“" + reply.Title + "” was removed. Replies already sent with it stay as they are.";
                }

                return RedirectToAction("CannedReplies");
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        // ---- service targets --------------------------------------------------------------------------------

        [HttpGet]
        [RequirePermission(Permission.ServiceTargets)]
        public IActionResult ServiceTargets()
        {
            return View(ServiceTargetsForm.From(Sla.Policy, Settings));
        }

        [HttpPost]
        [RequirePermission(Permission.ServiceTargets)]
        public async Task<IActionResult> ServiceTargets(ServiceTargetsForm form)
        {
            try
            {
                var problem = form.Problem();
                if (problem != null)
                {
                    ViewBag.Problem = problem;
                    Response.StatusCode = StatusCodes.Status400BadRequest;
                    return View(form);
                }

                var before = ServiceTargetsForm.From(Sla.Policy, Settings).Describe();
                await Settings.SaveAsync(Db, form.ToSettings(), CurrentUser!.FullName);
                var after = ServiceTargetsForm.From(Sla.Policy, Settings).Describe();
                if (before != after)
                {
                    Audit(AuditActions.SystemSettings, "System", null, "Service targets", "Now: " + after + " Before: " + before, null);
                    await Db.SaveChangesAsync();
                }

                TempData["SuccessMessage"] = before == after ? "Nothing was changed." : "Saved. Tickets raised from now on get these targets; tickets that exist keep theirs.";
                return RedirectToAction("ServiceTargets");
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        // ---- service report ---------------------------------------------------------------------------------

        /// <param name="days">The period: tickets raised in the last 7, 30, 90 or 365 days.</param>
        /// <param name="house">One brokerage house (XFL roles); a house admin always gets the own house.</param>
        [HttpGet]
        [RequirePermission(Permission.ServiceReport)]
        public async Task<IActionResult> ServiceReport(int days = 30, int house = 0)
        {
            try
            {
                if (days != 7 && days != 30 && days != 90 && days != 365) { days = 30; }
                var staff = Rbac.IsStaff(MyRole);
                var me = CurrentUser!;
                var houseId = staff ? house : me.BrokerageHouseName;

                var since = DateTime.Today.AddDays(-(days - 1));
                var ownWorkOnly = MyRole == Role.SupportEngineer && !Can(Permission.TicketWorkAny);
                var myId = me.Id;

                // which tickets the report is about: one house (or all), and for an engineer the own work
                IQueryable<IssueTable> Scope(IQueryable<IssueTable> all)
                {
                    if (houseId > 0) { all = all.Where(i => i.BrokerageId == houseId); }
                    if (ownWorkOnly) { all = all.Where(i => i.AssignedToId == myId); }
                    return all;
                }

                var tickets = await Scope(Db.Issues.Where(i => i.TDate >= since)).ToListAsync();
                var backlog = await Scope(Db.Issues.Where(TicketService.NotClosed)).ToListAsync();
                var solved = await Scope(Db.Issues.Where(i => i.ResolvedAt != null && i.ResolvedAt >= since)).ToListAsync();
                var houses = await Db.Brokerages.OrderBy(b => b.BrokerageHouseName).ToListAsync();
                var products = await Db.Products.ToDictionaryAsync(product => product.ProductId, product => product.Name);
                var report = ServiceReportBuilder.Build(tickets, backlog, Sla, houses, DateTime.Now, staff && !ownWorkOnly, products);
                ServiceReportBuilder.AddTrend(report, tickets, solved, since, DateTime.Today);
                report.Days = days;
                report.HouseId = houseId;
                report.HouseName = houseId > 0 ? houses.FirstOrDefault(b => b.BrokerageId == houseId)?.BrokerageHouseName : null;
                report.Houses = staff ? houses : new List<XFLCSMS.Models.Brocarage.Brokerage>();
                report.OwnWorkOnly = ownWorkOnly;
                return View(report);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }
    }
}
