using Microsoft.AspNetCore.StaticFiles;
using XFLCSMS.Infrastructure;
using XFLCSMS.Models.Issue;
using XFLCSMS.Models.Register;

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

        public TicketService(DataContext context, IWebHostEnvironment environment, AuditService audit)
        {
            _context = context;
            _environment = environment;
            _audit = audit;
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
                SupportTypeId = Existing(form.SupportTypeId, id => _context.SupportTypes.Any(x => x.SupportTypeId == id)),
                SupportCatagoryId = Existing(form.SupportCatagoryId, id => _context.SupportCatagories.Any(x => x.SupportCatagoryId == id)),
                SupportSubCatagoryId = Existing(form.SupportSubCatagoryID, id => _context.SupportSubCatagories.Any(x => x.SupportSubCatagoryId == id)),
                AffectedSectionId = Existing(form.AffectedSectionId, id => _context.AffectedSectionss.Any(x => x.AffectedSectionId == id)),
                IStatus = "Open",
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
            await _context.SaveChangesAsync();

            rejected = await SaveAttachmentsAsync(issue.IssueId, files);
            return (issue, null, rejected);
        }

        private static int? Existing(int? id, Func<int, bool> exists)
        {
            return id.HasValue && exists(id.Value) ? id : null;
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
        /// May the acting user set status and comments on this ticket? Platform admin and support manager: any ticket.
        /// Support engineer: the tickets assigned to him.
        /// </summary>
        public bool CanWorkOn(IssueTable issue)
        {
            if (_actor == null)
            {
                return false;
            }

            return Rbac.Can(_actorRole, Permission.TicketWorkAny)
                || (_actorRole == Role.SupportEngineer && IsAssignedTo(issue, _actor));
        }

        /// <summary>
        /// May the acting user change title, details, priority and files? Staff: as <see cref="CanWorkOn"/>.
        /// House admin: open tickets of the house. House user: the own open tickets.
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

            var open = issue.IStatus != "Close";
            return open && ((_actorRole == Role.HouseAdmin && issue.BrokerageId == _actor.BrokerageHouseName) || issue.UserId == _actor.Id);
        }

        // ---- assignment ------------------------------------------------------------------------

        /// <summary>
        /// Gives the ticket to an engineer, or to nobody (engineer == null). Stamps who assigned it and when,
        /// and writes the audit line. Returns the sentence that was logged, or null when nothing changed.
        /// </summary>
        public string? Assign(IssueTable issue, User? engineer)
        {
            var before = string.IsNullOrEmpty(issue.AssignBy) ? null : issue.AssignBy;
            var same = engineer == null
                ? IsUnassigned(issue)
                : issue.AssignedToId == engineer.Id || (issue.AssignedToId == null && issue.AssignBy == engineer.FullName);

            var now = DateTime.Now;
            if (engineer == null)
            {
                issue.AssignedToId = null;
                issue.AssignBy = null;
                issue.AssignOn = null;
                issue.ApproveBy = null;
                issue.ApproveOn = null;
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
            }
        }

        /// <summary>
        /// Edit rules for XFL staff: text fields, status, and (for roles that may assign) the engineer.
        /// The caller has checked <see cref="CanWorkOn"/>.
        /// </summary>
        public void ApplyStaffEdit(IssueTable issue, MakerView form, User editor, bool canApprove)
        {
            var changes = ApplyCommonFields(issue, form, editor);
            if (changes.Count > 0)
            {
                Log(AuditActions.TicketEdit, issue, "Changed " + string.Join(", ", changes));
            }

            // Assignment through the edit form: only for roles that may assign, and only when the form says so.
            // -1 means "leave as it is" (a ticket whose engineer is no longer in the list).
            if (canApprove && Rbac.Can(_actorRole, Permission.TicketAssign) && form.AssignedToId != KeepAssignee)
            {
                var engineer = form.AssignedToId == null ? null : Engineers().FirstOrDefault(u => u.Id == form.AssignedToId);
                if (form.AssignedToId == null || engineer != null)
                {
                    Assign(issue, engineer);
                }
            }

            SetStatus(issue, form.IStatus);
        }

        /// <summary>Sets the status (a missing value keeps the current one), stamps closing, writes the audit line.</summary>
        public void SetStatus(IssueTable issue, string? status)
        {
            var newStatus = string.IsNullOrWhiteSpace(status) || Array.IndexOf(Statuses, status) < 0 ? issue.IStatus : status;
            var oldStatus = issue.IStatus;
            var wasClosed = oldStatus == "Close";
            var now = DateTime.Now;
            issue.IStatus = newStatus;

            if (newStatus == "Close")
            {
                if (!wasClosed || issue.ClosedOn == null)
                {
                    issue.ClosedOn = now;
                    issue.ClosedBy = _actor?.FullName;
                }
            }
            else
            {
                issue.ClosedOn = null;
                issue.ClosedBy = null;
            }

            if (newStatus != oldStatus)
            {
                issue.UpdatedOn = now;
                issue.UpdatedBy = _actor?.FullName;
                Log(AuditActions.TicketStatus, issue,
                    (newStatus == "Close" ? "Closed the ticket" : wasClosed ? "Reopened the ticket as " + DisplayText.Status(newStatus) : "Status " + DisplayText.Status(newStatus))
                    + " (was " + DisplayText.Status(oldStatus) + ")");
            }
        }

        /// <summary>The stored status values, in the order of a ticket's life.</summary>
        public static readonly string[] Statuses = { "Open", "Inqueue", "Inprogress", "Close" };

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
