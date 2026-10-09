using Microsoft.AspNetCore.Mvc;
using XFLCSMS.Infrastructure;
using XFLCSMS.Models.Desk;
using XFLCSMS.Models.Issue;
using XFLCSMS.Services;

namespace XFLCSMS.Controllers
{
    // Keeping order among tickets (version 3.4), the same for every role:
    //   tags       labels AIPG staff put on tickets; the people of the brokerage houses do not see them
    //   links      two tickets that belong together (related, or one the duplicate of the other)
    //   watching   being told what happens on a ticket one neither raised nor works on
    //   automation the page of the rules that act by themselves
    public abstract partial class CsmsController
    {
        public const int MaxTagsPerTicket = 8;

        /// <summary>Fills the parts of the ticket page that came with version 3.4.</summary>
        private async Task FillOrganiseAsync(MakerView view, IssueTable issue)
        {
            var me = CurrentUser!;
            var staff = Rbac.IsStaff(MyRole);

            // who watches; the names are for AIPG staff
            var watchers = await Db.TicketWatchers.Where(row => row.IssueId == issue.IssueId).Select(row => row.UserId).ToListAsync();
            view.IsWatching = watchers.Contains(me.Id);
            // the person who raised it and its engineer are told anyway
            view.CanWatch = me.Id != issue.UserId && !TicketService.IsAssignedTo(issue, me);
            if (staff && watchers.Count > 0)
            {
                view.Watchers = await Db.Users.Where(user => watchers.Contains(user.Id)).OrderBy(user => user.FullName).Select(user => user.FullName).ToListAsync();
            }

            // links, from both sides; somebody of a brokerage house sees the ones to tickets he may open
            var links = await Db.TicketLinks.Where(link => link.IssueId == issue.IssueId || link.OtherIssueId == issue.IssueId).OrderBy(link => link.Id).ToListAsync();
            if (links.Count > 0)
            {
                var otherIds = links.Select(link => link.IssueId == issue.IssueId ? link.OtherIssueId : link.IssueId).Distinct().ToList();
                var others = await Db.Issues.Where(other => otherIds.Contains(other.IssueId)).ToListAsync();
                foreach (var link in links)
                {
                    var outgoing = link.IssueId == issue.IssueId;
                    var other = others.FirstOrDefault(row => row.IssueId == (outgoing ? link.OtherIssueId : link.IssueId));
                    if (other == null || !CanAccessIssue(other))
                    {
                        continue;
                    }

                    view.Links.Add(new MakerView.LinkedTicket
                    {
                        LinkId = link.Id,
                        IssueId = other.IssueId,
                        Number = other.TNumber,
                        Title = other.ITitle,
                        Status = other.IStatus,
                        What = link.Kind == TicketLink.Duplicate ? (outgoing ? "Duplicate of" : "Has the duplicate") : "Related to"
                    });
                }
            }

            if (!staff)
            {
                return;
            }

            view.Tags = await Db.TicketTags.Where(row => row.IssueId == issue.IssueId).Select(row => row.Tag!.Name).OrderBy(name => name).ToListAsync();
            view.AllTags = await Db.Tags.OrderBy(tag => tag.Name).Select(tag => tag.Name).ToListAsync();
            view.RelatedArticles = await RelatedArticlesAsync(issue);
            view.CanWriteKnowledge = WritesKnowledge;
        }

        // ---- tags -------------------------------------------------------------------------------------------

        /// <summary>A tag as it is stored: trimmed, one space between words, lower case; null when it cannot be a tag.</summary>
        public static string? CleanTag(string? text)
        {
            var name = System.Text.RegularExpressions.Regex.Replace((text ?? string.Empty).Trim(), "\\s+", " ").ToLowerInvariant();
            if (name.Length < 2 || name.Length > 40)
            {
                return null;
            }

            // letters and digits of any language, and a few signs; no comma (it separates tags) and no colon ("tag:" starts a search)
            return name.All(c => KnowledgeSearch.IsWordChar(c) || c == ' ' || c == '-' || c == '_' || c == '.' || c == '/' || c == '+' || c == '#') ? name : null;
        }

        /// <summary>Sets the tags of a ticket to the list typed (separated by commas). AIPG staff who may open the ticket.</summary>
        [HttpPost]
        public async Task<IActionResult> SetTags(int id, string? tags)
        {
            try
            {
                var issue = await Db.Issues.FirstOrDefaultAsync(i => i.IssueId == id);
                if (issue == null || !CanAccessIssue(issue))
                {
                    return NotFound();
                }

                if (!Rbac.IsStaff(MyRole))
                {
                    return Refused();
                }

                var typed = (tags ?? string.Empty).Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var wanted = typed.Select(CleanTag).Where(name => name != null).Select(name => name!).Distinct().ToList();
                var refused = typed.Where(text => CleanTag(text) == null).ToList();
                if (wanted.Count > MaxTagsPerTicket)
                {
                    TempData["ErrorMessage"] = "A ticket takes up to " + MaxTagsPerTicket + " tags. Nothing was changed.";
                    return Redirect(Url.Action("TicketView", new { id }) + "#tags");
                }

                var existing = await Db.Tags.Where(tag => wanted.Contains(tag.Name)).ToListAsync();
                foreach (var name in wanted.Where(name => existing.All(tag => tag.Name != name)))
                {
                    var tag = new Tag { Name = name };
                    Db.Tags.Add(tag);
                    existing.Add(tag);
                }

                var rows = await Db.TicketTags.Include(row => row.Tag).Where(row => row.IssueId == id).ToListAsync();
                var before = rows.Select(row => row.Tag!.Name).ToList();
                var added = wanted.Where(name => !before.Contains(name)).ToList();
                var removed = before.Where(name => !wanted.Contains(name)).ToList();
                Db.TicketTags.RemoveRange(rows.Where(row => removed.Contains(row.Tag!.Name)));
                foreach (var name in added)
                {
                    Db.TicketTags.Add(new TicketTag { IssueId = id, Tag = existing.First(tag => tag.Name == name) });
                }

                if (added.Count + removed.Count > 0)
                {
                    // a line only staff read (no brokerage house on it): tags are the support team's own
                    AuditTrailLog.Add(AuditActions.TicketTag, CurrentUser, Rbac.Label(MyRole), null, "Ticket", issue.IssueId, issue.TNumber,
                        "Tags: " + string.Join(", ", added.Select(name => "+" + name).Concat(removed.Select(name => "−" + name))));
                    try
                    {
                        await Db.SaveChangesAsync();
                    }
                    catch (DbUpdateException)
                    {
                        // somebody saved the same tag at the same moment (or the database holds two spellings to be one name)
                        TempData["ErrorMessage"] = "The tags were changed by somebody else just now. Look at them and save again.";
                        return Redirect(Url.Action("TicketView", new { id }) + "#tags");
                    }
                }

                if (refused.Count > 0)
                {
                    TempData["ErrorMessage"] = "Not a tag: " + string.Join(", ", refused.Select(text => "“" + text + "”"))
                        + ". A tag has 2 to 40 letters, digits, spaces or - _ . / + #.";
                }
                else if (added.Count + removed.Count > 0)
                {
                    TempData["SuccessMessage"] = "Tags saved.";
                }

                return Redirect(Url.Action("TicketView", new { id }) + "#tags");
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        /// <summary>All tags with the number of tickets that carry them: rename, merge by renaming, remove.</summary>
        [HttpGet]
        [RequirePermission(Permission.MasterData)]
        public async Task<IActionResult> TagList()
        {
            try
            {
                var tags = await Db.Tags.OrderBy(tag => tag.Name).ToListAsync();
                var counts = (await Db.TicketTags.GroupBy(row => row.TagId).Select(group => new { Id = group.Key, Count = group.Count() }).ToListAsync())
                    .ToDictionary(row => row.Id, row => row.Count);
                ViewBag.Counts = counts;
                return View(tags);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        /// <summary>Renames a tag. To the name of another tag: the two become one.</summary>
        [HttpPost]
        [RequirePermission(Permission.MasterData)]
        public async Task<IActionResult> RenameTag(int id, string? name)
        {
            try
            {
                var tag = await Db.Tags.FirstOrDefaultAsync(item => item.Id == id);
                if (tag == null)
                {
                    return NotFound();
                }

                var clean = CleanTag(name);
                if (clean == null)
                {
                    TempData["ErrorMessage"] = "A tag has 2 to 40 letters, digits, spaces or - _ . / + #.";
                    return RedirectToAction("TagList");
                }

                if (clean == tag.Name)
                {
                    return RedirectToAction("TagList");
                }

                var other = await Db.Tags.FirstOrDefaultAsync(item => item.Name == clean && item.Id != id);
                if (other != null)
                {
                    // merge: the tickets of this tag get the other one (unless they have it already), then this one goes
                    var mine = await Db.TicketTags.Where(row => row.TagId == id).ToListAsync();
                    var theirs = await Db.TicketTags.Where(row => row.TagId == other.Id).Select(row => row.IssueId).ToListAsync();
                    foreach (var row in mine.Where(row => !theirs.Contains(row.IssueId)))
                    {
                        Db.TicketTags.Add(new TicketTag { IssueId = row.IssueId, TagId = other.Id });
                    }

                    Db.TicketTags.RemoveRange(mine);
                    Db.Tags.Remove(tag);
                    Audit(AuditActions.DataUpdate, "Tag", other.Id, other.Name, "Merged the tag “" + tag.Name + "” into “" + other.Name + "”", null);
                    TempData["SuccessMessage"] = "“" + tag.Name + "” and “" + other.Name + "” are one tag now.";
                }
                else
                {
                    Audit(AuditActions.DataUpdate, "Tag", tag.Id, clean, "Renamed the tag “" + tag.Name + "” to “" + clean + "”", null);
                    tag.Name = clean;
                    TempData["SuccessMessage"] = "Renamed.";
                }

                try
                {
                    await Db.SaveChangesAsync();
                }
                catch (DbUpdateException)
                {
                    TempData.Remove("SuccessMessage");
                    TempData["ErrorMessage"] = "There is a tag with that name already (the database takes some spellings to be the same name). Nothing was changed.";
                }

                return RedirectToAction("TagList");
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [HttpPost]
        [RequirePermission(Permission.MasterData)]
        public async Task<IActionResult> DeleteTag(int id)
        {
            try
            {
                var tag = await Db.Tags.FirstOrDefaultAsync(item => item.Id == id);
                if (tag != null)
                {
                    var rows = await Db.TicketTags.Where(row => row.TagId == id).ToListAsync();
                    Db.TicketTags.RemoveRange(rows);
                    Db.Tags.Remove(tag);
                    Audit(AuditActions.DataDelete, "Tag", tag.Id, tag.Name, "Removed the tag “" + tag.Name + "”" + (rows.Count == 0 ? string.Empty : " from " + rows.Count + (rows.Count == 1 ? " ticket" : " tickets")), null);
                    await Db.SaveChangesAsync();
                    TempData["SuccessMessage"] = "“" + tag.Name + "” was removed.";
                }

                return RedirectToAction("TagList");
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        // ---- links ------------------------------------------------------------------------------------------

        /// <summary>Links this ticket to another one, named by its number. AIPG staff who may open both.</summary>
        /// <param name="kind">"related", or "duplicate": this ticket is a duplicate of the other.</param>
        [HttpPost]
        public async Task<IActionResult> LinkTicket(int id, string? number, string? kind)
        {
            try
            {
                var issue = await Db.Issues.FirstOrDefaultAsync(i => i.IssueId == id);
                if (issue == null || !CanAccessIssue(issue))
                {
                    return NotFound();
                }

                if (!Rbac.IsStaff(MyRole))
                {
                    return Refused();
                }

                kind = kind == TicketLink.Duplicate ? TicketLink.Duplicate : TicketLink.Related;
                // the number as people type it: "abc 42", "ABC_0000042", "abc-0000042"
                var typed = (number ?? string.Empty).Trim().ToUpperInvariant().Replace(' ', '_').Replace('-', '_');
                IssueTable? other = null;
                if (typed.Length > 0)
                {
                    other = await Db.Issues.FirstOrDefaultAsync(row => row.TNumber == typed);
                    var split = typed.LastIndexOf('_');
                    if (other == null && split > 0 && int.TryParse(typed.Substring(split + 1), out var serial))
                    {
                        var padded = typed.Substring(0, split + 1) + serial.ToString("D7");
                        other = await Db.Issues.FirstOrDefaultAsync(row => row.TNumber == padded);
                    }
                }

                string? problem = null;
                if (other == null || !CanAccessIssue(other)) { problem = "There is no ticket with the number “" + (number ?? string.Empty).Trim() + "”."; }
                else if (other.IssueId == issue.IssueId) { problem = "A ticket cannot be linked to itself."; }
                if (problem != null)
                {
                    TempData["ErrorMessage"] = problem;
                    return Redirect(Url.Action("TicketView", new { id }) + "#links");
                }

                var link = await Db.TicketLinks.FirstOrDefaultAsync(row =>
                    (row.IssueId == issue.IssueId && row.OtherIssueId == other!.IssueId) || (row.IssueId == other!.IssueId && row.OtherIssueId == issue.IssueId));
                if (link == null)
                {
                    link = new TicketLink { CreatedAt = DateTime.Now, CreatedBy = CurrentUser!.FullName };
                    Db.TicketLinks.Add(link);
                }

                link.IssueId = issue.IssueId;
                link.OtherIssueId = other!.IssueId;
                link.Kind = kind;
                // one line on each ticket, for staff only: the other ticket may belong to another brokerage house
                AuditTrailLog.Add(AuditActions.TicketLink, CurrentUser, Rbac.Label(MyRole), null, "Ticket", issue.IssueId, issue.TNumber,
                    kind == TicketLink.Duplicate ? "Marked as a duplicate of " + other.TNumber : "Linked to " + other.TNumber);
                AuditTrailLog.Add(AuditActions.TicketLink, CurrentUser, Rbac.Label(MyRole), null, "Ticket", other.IssueId, other.TNumber,
                    kind == TicketLink.Duplicate ? issue.TNumber + " was marked as a duplicate of this ticket" : "Linked to " + issue.TNumber);
                await Db.SaveChangesAsync();

                TempData["SuccessMessage"] = kind == TicketLink.Duplicate
                    ? issue.TNumber + " is marked as a duplicate of " + other.TNumber + ". Close it when the people of the house know where the work goes on."
                    : "Linked to " + other.TNumber + ".";
                return Redirect(Url.Action("TicketView", new { id }) + "#links");
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [HttpPost]
        public async Task<IActionResult> UnlinkTicket(int id, int linkId)
        {
            try
            {
                var issue = await Db.Issues.FirstOrDefaultAsync(i => i.IssueId == id);
                if (issue == null || !CanAccessIssue(issue))
                {
                    return NotFound();
                }

                if (!Rbac.IsStaff(MyRole))
                {
                    return Refused();
                }

                var link = await Db.TicketLinks.FirstOrDefaultAsync(row => row.Id == linkId && (row.IssueId == id || row.OtherIssueId == id));
                if (link != null)
                {
                    var otherId = link.IssueId == id ? link.OtherIssueId : link.IssueId;
                    var other = await Db.Issues.FirstOrDefaultAsync(row => row.IssueId == otherId);
                    Db.TicketLinks.Remove(link);
                    AuditTrailLog.Add(AuditActions.TicketLink, CurrentUser, Rbac.Label(MyRole), null, "Ticket", issue.IssueId, issue.TNumber, "Removed the link to " + (other == null ? "a deleted ticket" : CanAccessIssue(other) ? other.TNumber : "another ticket"));
                    if (other != null)
                    {
                        AuditTrailLog.Add(AuditActions.TicketLink, CurrentUser, Rbac.Label(MyRole), null, "Ticket", other.IssueId, other.TNumber, "Removed the link to " + issue.TNumber);
                    }

                    await Db.SaveChangesAsync();
                    TempData["SuccessMessage"] = "The link was removed.";
                }

                return Redirect(Url.Action("TicketView", new { id }) + "#links");
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        // ---- watching ---------------------------------------------------------------------------------------

        /// <summary>Start or stop being told about a ticket. Everybody who may open it.</summary>
        [HttpPost]
        public async Task<IActionResult> WatchTicket(int id, bool on)
        {
            try
            {
                var issue = await Db.Issues.FirstOrDefaultAsync(i => i.IssueId == id);
                if (issue == null || !CanAccessIssue(issue))
                {
                    return NotFound();
                }

                var me = CurrentUser!;
                var row = await Db.TicketWatchers.FirstOrDefaultAsync(item => item.IssueId == id && item.UserId == me.Id);
                if (on && row == null)
                {
                    Db.TicketWatchers.Add(new TicketWatcher { IssueId = id, UserId = me.Id, Since = DateTime.Now });
                    try { await Db.SaveChangesAsync(); }
                    catch (DbUpdateException) { /* pressed twice at the same moment: watching already */ }
                    TempData["SuccessMessage"] = "You watch ticket " + issue.TNumber + " now: you are told about replies and when its status changes.";
                }
                else if (!on && row != null)
                {
                    Db.TicketWatchers.Remove(row);
                    await Db.SaveChangesAsync();
                    TempData["SuccessMessage"] = "You no longer watch ticket " + issue.TNumber + ".";
                }

                return RedirectToAction("TicketView", new { id });
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        /// <summary>The tickets the signed-in user watches.</summary>
        public Task<IActionResult> WatchedTicketList(int page, int rowperpage, string? searchString = null, string? sortField = null, bool sortAscending = true)
        {
            var myId = CurrentUser?.Id ?? 0;
            var watched = Db.TicketWatchers.Where(row => row.UserId == myId).Select(row => row.IssueId);
            return TicketListPage(tickets => tickets.Where(issue => watched.Contains(issue.IssueId)),
                page, rowperpage, searchString, sortField, sortAscending, byStatus: false);
        }

        // ---- automation -------------------------------------------------------------------------------------

        protected AutomationRules Rules => HttpContext.RequestServices.GetRequiredService<AutomationRules>();

        private async Task FillAutomationPageAsync()
        {
            var engineers = await Tickets.Engineers().OrderBy(user => user.FullName).Select(user => new { user.Id, user.FullName }).ToListAsync();
            var products = await Db.Products.OrderBy(product => product.Name).ToListAsync();
            ViewBag.EngineerCount = engineers.Count;
            ViewBag.ProductEngineers = products.Where(product => product.EngineerId != null)
                .Select(product => (product, engineers.FirstOrDefault(user => user.Id == product.EngineerId)?.FullName)).ToList();
            ViewBag.ProductCount = products.Count;
            var worker = HttpContext.RequestServices.GetRequiredService<AutomationWorker>();
            ViewBag.LastRun = worker.LastRun;
            ViewBag.MaxPerCheck = worker.MaxPerRun;

            // how many tickets are deployed / pending at all (that is what a rule meets when it is switched on),
            // and how many of them the rules as saved would close at the next checks
            var now = DateTime.Now;
            var rules = Rules;
            var deployed = await Db.Issues.Where(issue => issue.IStatus == TicketStatus.Deployed).ToListAsync();
            var pendingNow = await Db.Issues.Where(issue => issue.IStatus == TicketStatus.Pending).ToListAsync();
            ViewBag.DeployedCount = deployed.Count;
            ViewBag.DeployedOldest = deployed.Count == 0 ? (DateTime?)null : deployed.Min(AutomationWorker.Since);
            ViewBag.PendingCount = pendingNow.Count;
            ViewBag.PendingOldest = pendingNow.Count == 0 ? (DateTime?)null : pendingNow.Min(AutomationWorker.PendingFrom);
            ViewBag.CloseDue = rules.CloseDays > 0 ? deployed.Count(issue => AutomationWorker.Since(issue) <= now.AddDays(-rules.CloseDays)) : 0;
            ViewBag.PendingCloseDue = rules.PendingCloseDays > 0 ? pendingNow.Count(issue => AutomationWorker.PendingFrom(issue) <= now.AddDays(-rules.PendingCloseDays)) : 0;
            ViewBag.LastResult = worker.LastResult;
            ViewBag.LastError = worker.LastError;
        }

        [HttpGet]
        [RequirePermission(Permission.Automation)]
        public async Task<IActionResult> Automation()
        {
            try
            {
                await FillAutomationPageAsync();
                return View(AutomationForm.From(Rules));
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [HttpPost]
        [RequirePermission(Permission.Automation)]
        public async Task<IActionResult> Automation(AutomationForm form)
        {
            try
            {
                var problem = form.Problem();
                if (problem != null)
                {
                    ViewBag.Problem = problem;
                    await FillAutomationPageAsync();
                    Response.StatusCode = StatusCodes.Status400BadRequest;
                    return View(form);
                }

                var before = AutomationForm.From(Rules).Describe();
                await Settings.SaveAsync(Db, form.ToSettings(), CurrentUser!.FullName);
                var after = AutomationForm.From(Rules).Describe();
                if (before != after)
                {
                    Audit(AuditActions.SystemSettings, "System", null, "Automation", "Now: " + after + " Before: " + before, null);
                    await Db.SaveChangesAsync();
                }

                TempData["SuccessMessage"] = before == after ? "Nothing was changed." : "Saved. The rules apply from now on.";
                return RedirectToAction("Automation");
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }
    }
}
