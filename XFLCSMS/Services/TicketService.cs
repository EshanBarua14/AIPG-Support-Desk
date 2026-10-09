using Microsoft.AspNetCore.StaticFiles;
using XFLCSMS.Infrastructure;
using XFLCSMS.Models.Issue;
using XFLCSMS.Models.Register;
using XFLCSMS.Services.Notify;

namespace XFLCSMS.Services
{
    /// <summary>
    /// Ticket logic that used to be copy-pasted (with different bugs) into every role controller:
    /// ticket numbering, ticket creation, attachment storage/lookup and the staff "edit ticket" rules.
    /// </summary>
    public class TicketService
    {
        public const string UploadFolderName = "Uplods";

        /// <summary>File types that may be attached to a ticket (the folder is served from wwwroot).</summary>
        public static readonly string[] AllowedExtensions =
        {
            ".txt", ".doc", ".docx", ".pdf", ".jpg", ".jpeg", ".png", ".xls", ".xlsx", ".csv"
        };

        private static readonly FileExtensionContentTypeProvider ContentTypes = new();

        private readonly DataContext _context;
        private readonly IWebHostEnvironment _environment;
        private readonly AuditService _audit;
        private readonly NotificationService _notify;

        public TicketService(DataContext context, IWebHostEnvironment environment, AuditService audit, NotificationService notify)
        {
            _context = context;
            _environment = environment;
            _audit = audit;
            _notify = notify;
        }

        public string UploadFolder =>
            Path.Combine(_environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot"), UploadFolderName);

        /// <summary>Next free ticket number for a brokerage house, e.g. ABC_0000042. Null when the house does not exist.</summary>
        public string? NextTicketNumber(int brokerageId)
        {
            var house = _context.Brokerages.FirstOrDefault(b => b.BrokerageId == brokerageId);
            if (house == null)
            {
                return null;
            }

            var prefix = house.BrokerageHouseAcronym + "_";
            var max = 0;
            var numbers = _context.Issues
                .Where(i => i.TNumber.StartsWith(prefix))
                .Select(i => i.TNumber)
                .ToList();

            foreach (var number in numbers)
            {
                // TryParse: one malformed ticket number must not break ticket creation for everybody.
                if (int.TryParse(number.Substring(prefix.Length), out var value) && value > max)
                {
                    max = value;
                }
            }

            return prefix + (max + 1).ToString("D7");
        }

        /// <summary>
        /// Creates a ticket for the logged-in user. Owner, brokerage house, ticket number and creation time
        /// always come from the server, never from the (editable) form fields.
        /// </summary>
        public async Task<(IssueTable? Issue, string? Error, List<string> RejectedFiles)> CreateAsync(
            User user, IssueFrom? form, IEnumerable<IFormFile>? files)
        {
            var rejected = new List<string>();

            if (form == null)
            {
                return (null, "The ticket form was empty. Please fill it in again.", rejected);
            }

            if (string.IsNullOrWhiteSpace(form.ITitle))
            {
                return (null, "Please enter an issue title.", rejected);
            }

            if (string.IsNullOrWhiteSpace(form.Priority))
            {
                return (null, "Please select a priority.", rejected);
            }

            var ticketNumber = NextTicketNumber(user.BrokerageHouseName);
            if (ticketNumber == null)
            {
                return (null, "Your account is not linked to a valid brokerage house. Please contact the XFL team.", rejected);
            }

            // What the ticket is about. Of the four lists only an entry of the chosen product, or one that is for
            // every product, is kept; without a chosen product, the product of the first entry that has one counts.
            var type = form.SupportTypeId == null ? null : _context.SupportTypes.AsNoTracking().FirstOrDefault(x => x.SupportTypeId == form.SupportTypeId);
            var category = form.SupportCatagoryId == null ? null : _context.SupportCatagories.AsNoTracking().FirstOrDefault(x => x.SupportCatagoryId == form.SupportCatagoryId);
            var subCategory = form.SupportSubCatagoryID == null ? null : _context.SupportSubCatagories.AsNoTracking().FirstOrDefault(x => x.SupportSubCatagoryId == form.SupportSubCatagoryID);
            var section = form.AffectedSectionId == null ? null : _context.AffectedSectionss.AsNoTracking().FirstOrDefault(x => x.AffectedSectionId == form.AffectedSectionId);
            var productId = ActiveProduct(form.ProductId)
                ?? ActiveProduct(type?.ProductId ?? category?.ProductId ?? subCategory?.ProductId ?? section?.ProductId);
            bool Fits(int? productOfEntry) => productOfEntry == null || productOfEntry == productId;

            var issue = new IssueTable
            {
                TDate = DateTime.Now,
                TNumber = ticketNumber,
                Priority = form.Priority,
                ITitle = form.ITitle.Trim(),
                Details = CleanRichText(form.IssueDetails),
                Comments = form.Commands,
                UserId = user.Id,
                BrokerageId = user.BrokerageHouseName,
                ProductId = productId,
                SupportTypeId = type != null && Fits(type.ProductId) ? type.SupportTypeId : null,
                SupportCatagoryId = category != null && Fits(category.ProductId) ? category.SupportCatagoryId : null,
                SupportSubCatagoryId = subCategory != null && Fits(subCategory.ProductId) ? subCategory.SupportSubCatagoryId : null,
                AffectedSectionId = section != null && Fits(section.ProductId) ? section.AffectedSectionId : null,
                IStatus = TicketStatus.Unassigned,
                AssignOn = null,
                AssignBy = null
            };

            if (Array.IndexOf(Priorities, issue.Priority) < 0)
            {
                return (null, "Please select a priority.", rejected);
            }

            _context.Issues.Add(issue);
            await _context.SaveChangesAsync(); // the ticket gets its id here

            Log(AuditActions.TicketCreate, issue, "Raised the ticket \u201c" + issue.ITitle + "\u201d, priority " + issue.Priority);
            _notify.TicketRaised(issue, user);
            await _context.SaveChangesAsync();

            rejected = await SaveAttachmentsAsync(issue.IssueId, files);
            return (issue, null, rejected);
        }

        /// <summary>The id when it is a product tickets can be raised for (it exists and is active), else null.</summary>
        private int? ActiveProduct(int? id)
        {
            return id.HasValue && _context.Products.Any(product => product.ProductId == id && product.IsActive) ? id : null;
        }

        /// <summary>Stores uploaded files for a ticket. Returns the names of files that were refused.</summary>
        public async Task<List<string>> SaveAttachmentsAsync(int issueId, IEnumerable<IFormFile>? files)
        {
            var rejected = new List<string>();
            if (files == null)
            {
                return rejected;
            }

            Directory.CreateDirectory(UploadFolder);
            var added = false;
            var names = new List<string>();

            foreach (var file in files)
            {
                if (file == null || file.Length == 0)
                {
                    continue;
                }

                // Keep only the file name: browsers may send a full client path and "..\\" must never reach the disk.
                var originalName = Path.GetFileName((file.FileName ?? string.Empty).Replace('\\', '/'));
                var extension = Path.GetExtension(originalName).ToLowerInvariant();

                if (string.IsNullOrWhiteSpace(originalName) || !AllowedExtensions.Contains(extension))
                {
                    rejected.Add(string.IsNullOrWhiteSpace(originalName) ? "(unnamed file)" : originalName);
                    continue;
                }

                // Unique name on disk so two tickets with "screenshot.png" no longer overwrite each other.
                var storedName = Guid.NewGuid().ToString("N") + extension;
                var filePath = Path.Combine(UploadFolder, storedName);

                await using (var stream = new FileStream(filePath, FileMode.CreateNew))
                {
                    await file.CopyToAsync(stream);
                }

                _context.Attachments.Add(new Attachment
                {
                    FileName = originalName,
                    AttachmentLoc = filePath,
                    IssueId = issueId
                });
                names.Add(originalName);
                added = true;
            }

            if (added)
            {
                var issue = await _context.Issues.FirstOrDefaultAsync(i => i.IssueId == issueId);
                if (issue != null)
                {
                    Log(AuditActions.TicketFileAdd, issue, "Attached " + string.Join(", ", names));
                    if (issue.TDate < DateTime.Now.AddMinutes(-1)) // files that come with a new ticket are part of "ticket raised"
                    {
                        _notify.TicketEdited(issue, "Attached " + string.Join(", ", names), _actor);
                    }
                }

                await _context.SaveChangesAsync();
            }

            return rejected;
        }

        /// <summary>
        /// Finds the attachment on disk. AttachmentLoc holds an absolute path from the machine that saved it, so after
        /// restoring the database on another PC/server we fall back to this installation's upload folder.
        /// </summary>
        public string? ResolveAttachmentPath(Attachment attachment)
        {
            if (!string.IsNullOrWhiteSpace(attachment.AttachmentLoc) && File.Exists(attachment.AttachmentLoc))
            {
                return attachment.AttachmentLoc;
            }

            foreach (var name in new[] { LastSegment(attachment.AttachmentLoc), LastSegment(attachment.FileName) })
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var candidate = Path.Combine(UploadFolder, name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        public static string GetContentType(string fileName)
        {
            return ContentTypes.TryGetContentType(fileName, out var contentType) ? contentType : "application/octet-stream";
        }

        /// <summary>Removes the attachment row and, when no other ticket uses the same file, the file itself.</summary>
        /// <param name="quiet">True when the whole ticket is being deleted: that is one audit line, not one per file.</param>
        public async Task DeleteAttachmentAsync(Attachment attachment, bool quiet = false)
        {
            var path = ResolveAttachmentPath(attachment);
            var location = attachment.AttachmentLoc;

            var owner = attachment.issue ?? await _context.Issues.FirstOrDefaultAsync(i => i.IssueId == attachment.IssueId);
            if (owner != null && !quiet)
            {
                Log(AuditActions.TicketFileDelete, owner, "Removed the file " + attachment.FileName);
            }

            _context.Attachments.Remove(attachment);
            await _context.SaveChangesAsync();

            if (path == null)
            {
                return;
            }

            var stillUsed = _context.Attachments.Any(a => a.AttachmentLoc == location);
            var insideUploadFolder = Path.GetFullPath(path).StartsWith(Path.GetFullPath(UploadFolder), StringComparison.OrdinalIgnoreCase);
            if (!stillUsed && insideUploadFolder)
            {
                try
                {
                    File.Delete(path);
                }
                catch (IOException)
                {
                    // The row is gone; a locked file can be cleaned up later.
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        // ---- who may do what on a ticket --------------------------------------------------------

        /// <summary>
        /// Tickets assigned to this engineer. The user id decides; tickets assigned before the id was stored
        /// carry the engineer's name only.
        /// </summary>
        public static System.Linq.Expressions.Expression<Func<IssueTable, bool>> AssignedTo(User engineer)
        {
            var id = engineer.Id;
            var name = engineer.FullName;
            return issue => issue.AssignedToId == id || (issue.AssignedToId == null && issue.AssignBy == name);
        }

        public static bool IsAssignedTo(IssueTable issue, User engineer)
        {
            return issue.AssignedToId == engineer.Id || (issue.AssignedToId == null && !string.IsNullOrEmpty(issue.AssignBy) && issue.AssignBy == engineer.FullName);
        }

        /// <summary>Tickets without an engineer (for queries).</summary>
        public static readonly System.Linq.Expressions.Expression<Func<IssueTable, bool>> Unassigned =
            issue => issue.AssignedToId == null && (issue.AssignBy == null || issue.AssignBy == "");

        /// <summary>Tickets that are not closed (for queries).</summary>
        public static readonly System.Linq.Expressions.Expression<Func<IssueTable, bool>> NotClosed =
            issue => issue.IStatus != TicketStatus.Closed;

        public static bool IsUnassigned(IssueTable issue)
        {
            return issue.AssignedToId == null && string.IsNullOrEmpty(issue.AssignBy);
        }

        /// <summary>Support engineers a ticket can be given to: XFL staff with that position whose account works.</summary>
        public IQueryable<User> Engineers()
        {
            return _context.Users.Where(u => u.UType && !u.UCatagory && u.Department == Rbac.EngineerPosition && u.UStatus && u.VerifiedAt != null);
        }

        /// <summary>
        /// May the acting user set status and comments on this ticket? XFL staff only: with "work on any ticket"
        /// every ticket, otherwise the tickets assigned to him.
        /// </summary>
        public bool CanWorkOn(IssueTable issue)
        {
            if (_actor == null)
            {
                return false;
            }

            return Rbac.IsStaff(_actorRole)
                && (Rbac.Can(_actorRole, Permission.TicketWorkAny) || IsAssignedTo(issue, _actor));
        }

        /// <summary>
        /// May the acting user change title, details, priority and files? Staff: as <see cref="CanWorkOn"/>.
        /// People of a brokerage house: the own open tickets, and with "edit the tickets of the own house" every open
        /// ticket of the house.
        /// </summary>
        public bool CanEdit(IssueTable issue)
        {
            if (_actor == null)
            {
                return false;
            }

            if (Rbac.IsStaff(_actorRole))
            {
                return CanWorkOn(issue);
            }

            var open = !TicketStatus.IsClosed(issue.IStatus);
            return open && ((Rbac.Can(_actorRole, Permission.TicketEditHouse) && issue.BrokerageId == _actor.BrokerageHouseName) || issue.UserId == _actor.Id);
        }

        // ---- assignment ------------------------------------------------------------------------

        /// <summary>
        /// Gives the ticket to an engineer, or to nobody (engineer == null). Stamps who assigned it and when,
        /// and writes the audit line. Returns the sentence that was logged, or null when nothing changed.
        /// </summary>
        public string? Assign(IssueTable issue, User? engineer)
        {
            var before = string.IsNullOrEmpty(issue.AssignBy) ? null : issue.AssignBy;
            var beforeId = issue.AssignedToId ?? (before == null ? null : Engineers().Where(u => u.FullName == before).Select(u => (int?)u.Id).FirstOrDefault());
            var same = engineer == null
                ? IsUnassigned(issue)
                : issue.AssignedToId == engineer.Id || (issue.AssignedToId == null && issue.AssignBy == engineer.FullName);

            var now = DateTime.Now;
            var closed = TicketStatus.IsClosed(issue.IStatus);
            if (engineer == null)
            {
                issue.AssignedToId = null;
                issue.AssignBy = null;
                issue.AssignOn = null;
                issue.ApproveBy = null;
                issue.ApproveOn = null;
                if (!closed)
                {
                    issue.IStatus = TicketStatus.Unassigned; // a ticket without an engineer is "Unassigned", whatever it was
                }
            }
            else
            {
                issue.AssignedToId = engineer.Id; // also fills the id in on tickets that only had the name
                issue.AssignBy = engineer.FullName;
                if (!same || issue.AssignOn == null)
                {
                    issue.AssignOn = now;
                    issue.ApproveOn = now;
                    issue.ApproveBy = _actor?.FullName;
                }

                if (!closed && !TicketStatus.Work.Contains(TicketStatus.Normalize(issue.IStatus)))
                {
                    issue.IStatus = TicketStatus.Assigned;
                }
            }

            if (same)
            {
                return null;
            }

            issue.UpdatedOn = now;
            issue.UpdatedBy = _actor?.FullName;

            string sentence;
            if (engineer == null)
            {
                sentence = _actor != null && before == _actor.FullName ? "Gave the ticket back (was assigned to " + before + ")" : "Unassigned (was " + before + ")";
            }
            else if (_actor != null && engineer.Id == _actor.Id)
            {
                sentence = "Took the ticket" + (before == null ? string.Empty : " (was assigned to " + before + ")");
            }
            else
            {
                sentence = "Assigned to " + engineer.FullName + (before == null ? string.Empty : " (was " + before + ")");
            }

            Log(engineer == null ? AuditActions.TicketUnassign : AuditActions.TicketAssign, issue, sentence);
            if (engineer == null)
            {
                _notify.TicketUnassigned(issue, beforeId, before, _actor);
            }
            else
            {
                _notify.TicketAssigned(issue, engineer, beforeId, _actor);
            }

            return sentence;
        }

        // ---- editing ---------------------------------------------------------------------------

        /// <summary>Edit rules for the people of the brokerage house (house user, house admin): text fields and priority only.</summary>
        public void ApplyOwnerEdit(IssueTable issue, MakerView form, User editor)
        {
            var changes = ApplyCommonFields(issue, form, editor);
            if (changes.Count > 0)
            {
                Log(AuditActions.TicketEdit, issue, "Changed " + string.Join(", ", changes));
                _notify.TicketEdited(issue, "Changed " + string.Join(", ", changes), _actor);
            }
        }

        /// <summary>
        /// Edit rules for XFL staff: text fields, status, and (for roles that may assign) the engineer.
        /// The caller has checked <see cref="CanWorkOn"/>. Returns what was asked for but not done, as sentences
        /// for the user (everything else is applied all the same); empty when everything was done.
        ///
        /// The form posts the status and the engineer it showed when it was opened. Only a field the user really
        /// changed is a request: a form that sat open while a colleague took the ticket and started on it must not
        /// take the ticket away again just because somebody adds a comment.
        /// </summary>
        public List<string> ApplyStaffEdit(IssueTable issue, MakerView form, User editor, bool canApprove)
        {
            var notes = new List<string>();
            var statusBefore = TicketStatus.Normalize(issue.IStatus);
            var changes = ApplyCommonFields(issue, form, editor);
            if (changes.Count > 0)
            {
                Log(AuditActions.TicketEdit, issue, "Changed " + string.Join(", ", changes));
                _notify.TicketEdited(issue, "Changed " + string.Join(", ", changes), _actor);
            }

            // what the form showed; a page from before these fields existed posts none: then the ticket as it is now counts
            var wanted = TicketStatus.Normalize(form.IStatus);
            var statusAsked = wanted != null && wanted != (TicketStatus.Normalize(form.OriginalStatus) ?? statusBefore);
            var assigneeAsked = form.AssignedToId != KeepAssignee
                && (form.OriginalAssignee == null || form.OriginalAssignee != (form.AssignedToId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? NobodyAssigned));

            var wasClosed = statusBefore == TicketStatus.Closed;
            var reopen = wasClosed && statusAsked && wanted != TicketStatus.Closed && Rbac.Can(_actorRole, Permission.TicketClose);

            // Assignment through the edit form: only for roles that may assign. A closed ticket keeps its engineer
            // unless it is being reopened in the same step.
            if (canApprove && Rbac.Can(_actorRole, Permission.TicketAssign) && assigneeAsked)
            {
                var engineer = form.AssignedToId == null ? null : Engineers().FirstOrDefault(u => u.Id == form.AssignedToId);
                if (wasClosed && !reopen)
                {
                    if (!(engineer == null ? IsUnassigned(issue) : IsAssignedTo(issue, engineer)))
                    {
                        notes.Add("the engineer was not changed: the ticket is closed. Reopen it first.");
                    }
                }
                else if (form.AssignedToId == null || engineer != null)
                {
                    Assign(issue, engineer);
                }
            }

            if (!statusAsked || wanted == TicketStatus.Normalize(issue.IStatus))
            {
                return notes;
            }

            if (reopen)
            {
                // a reopened ticket gets the status its assignment allows
                if (IsUnassigned(issue)) { wanted = TicketStatus.Unassigned; }
                else if (wanted == TicketStatus.Unassigned) { wanted = TicketStatus.Assigned; }
            }

            var refusal = SetStatus(issue, wanted);
            if (refusal != null)
            {
                notes.Add("the status was not changed. " + refusal);
            }

            return notes;
        }

        /// <summary>
        /// Sets the status, stamps closing, writes the audit line and tells the people concerned.
        /// Returns null when the ticket has the status now, otherwise the reason it was refused:
        ///   - "Unassigned" is the status of a ticket without an engineer (and only of such a ticket);
        ///   - every working status needs an engineer;
        ///   - Deployed, Closed and reopening need the permission "deploy, close and reopen".
        /// </summary>
        public string? SetStatus(IssueTable issue, string? status)
        {
            var newStatus = TicketStatus.Normalize(status);
            var oldStatus = TicketStatus.Normalize(issue.IStatus) ?? (IsUnassigned(issue) ? TicketStatus.Unassigned : TicketStatus.Assigned);
            if (newStatus == null || newStatus == oldStatus)
            {
                return null;
            }

            var wasClosed = oldStatus == TicketStatus.Closed;
            var refusal = WhyNot(issue, oldStatus, newStatus);
            if (refusal != null)
            {
                return refusal;
            }

            var now = DateTime.Now;
            issue.IStatus = newStatus;
            if (newStatus == TicketStatus.Closed)
            {
                issue.ClosedOn = now;
                issue.ClosedBy = _actor?.FullName;
            }
            else
            {
                issue.ClosedOn = null;
                issue.ClosedBy = null;
            }

            issue.UpdatedOn = now;
            issue.UpdatedBy = _actor?.FullName;
            Log(AuditActions.TicketStatus, issue,
                (newStatus == TicketStatus.Closed ? "Closed the ticket" : wasClosed ? "Reopened the ticket as " + TicketStatus.Name(newStatus) : "Status " + TicketStatus.Name(newStatus))
                + " (was " + TicketStatus.Name(oldStatus) + ")");
            _notify.TicketStatusChanged(issue, oldStatus, _actor);
            return null;
        }

        private string? WhyNot(IssueTable issue, string oldStatus, string newStatus)
        {
            var name = TicketStatus.Name(newStatus);
            if ((TicketStatus.NeedsClosePermission(newStatus) || oldStatus == TicketStatus.Closed) && !Rbac.Can(_actorRole, Permission.TicketClose))
            {
                return oldStatus == TicketStatus.Closed
                    ? "Your role cannot reopen a closed ticket."
                    : "Your role cannot set a ticket to " + name + ".";
            }

            var free = IsUnassigned(issue);
            if (newStatus == TicketStatus.Unassigned && !free)
            {
                return "Unassigned is the status of a ticket without an engineer. Unassign the ticket instead.";
            }

            if (free && TicketStatus.Work.Contains(newStatus))
            {
                return "Assign the ticket to an engineer before setting it to " + name + ".";
            }

            return null;
        }

        /// <summary>The statuses the acting user may give this ticket now (without the one it has).</summary>
        public List<TicketStatus.Info> StatusChoices(IssueTable issue)
        {
            var current = TicketStatus.Normalize(issue.IStatus) ?? (IsUnassigned(issue) ? TicketStatus.Unassigned : TicketStatus.Assigned);
            if (!CanWorkOn(issue))
            {
                return new List<TicketStatus.Info>();
            }

            return TicketStatus.All.Where(item => item.Key != current && WhyNot(issue, current, item.Key) == null).ToList();
        }

        /// <summary>The stored status values, in the order of a ticket's life.</summary>
        public static readonly string[] Statuses = TicketStatus.Keys;

        /// <summary>
        /// What the hidden field "OriginalAssignee" of the edit form holds for a ticket without an engineer.
        /// (Not the empty string: an empty form field arrives as "not posted".)
        /// </summary>
        public const string NobodyAssigned = "none";

        /// <summary>Value of the "Assigned to" field that means: do not touch the assignment.</summary>
        public const int KeepAssignee = -1;

        private static List<string> ApplyCommonFields(IssueTable issue, MakerView form, User editor)
        {
            var changes = new List<string>();

            if (!string.IsNullOrWhiteSpace(form.IssueTitle) && form.IssueTitle.Trim() != issue.ITitle)
            {
                issue.ITitle = form.IssueTitle.Trim();
                changes.Add("the title");
            }

            // Rich text from the editor: cleaned before it is stored and again when it is shown.
            var details = CleanRichText(form.TicketDetails);
            if (details != issue.Details)
            {
                issue.Details = details;
                changes.Add("the details");
            }

            var comments = string.IsNullOrWhiteSpace(form.Command) ? null : form.Command;
            if (comments != (string.IsNullOrWhiteSpace(issue.Comments) ? null : issue.Comments))
            {
                issue.Comments = form.Command;
                changes.Add("the comments");
            }

            if (!string.IsNullOrWhiteSpace(form.Priority) && form.Priority != issue.Priority && Array.IndexOf(Priorities, form.Priority) >= 0)
            {
                changes.Add("the priority from " + issue.Priority + " to " + form.Priority);
                issue.Priority = form.Priority;
            }

            if (changes.Count > 0)
            {
                issue.UpdatedOn = DateTime.Now;
                issue.UpdatedBy = editor.FullName;
            }

            return changes;
        }

        public static readonly string[] Priorities = { "Low", "Medium", "High" };

        // ---- audit -----------------------------------------------------------------------------

        private User? _actor;
        private Role _actorRole = Role.HouseUser;

        /// <summary>Tells the service who is acting in this request (set once by CsmsController).</summary>
        public void ActAs(User user, Role role)
        {
            _actor = user;
            _actorRole = role;
        }

        /// <summary>One line in the history of a ticket. It is saved with the next SaveChanges.</summary>
        public void Log(string action, IssueTable issue, string sentence)
        {
            _audit.Add(action, _actor, Rbac.Label(_actorRole), issue.BrokerageId, "Ticket", issue.IssueId, issue.TNumber, sentence);
        }

        // Empty stays empty (null); anything else is stored without scripts, event handlers and page-breaking styles.
        private static string? CleanRichText(string? html)
        {
            return string.IsNullOrWhiteSpace(html) ? null : HtmlSanitizer.Sanitize(html);
        }

        private static string LastSegment(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            return path.Substring(path.LastIndexOfAny(new[] { '/', '\\' }) + 1);
        }
    }
}
