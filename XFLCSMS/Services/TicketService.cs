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
        private readonly UploadLimits _uploads;
        private readonly SlaService _sla;
        private readonly AutomationRules _rules;

        public TicketService(DataContext context, IWebHostEnvironment environment, AuditService audit, NotificationService notify, UploadLimits uploads, SlaService sla, AutomationRules rules)
        {
            _sla = sla;
            _rules = rules;
            _context = context;
            _environment = environment;
            _audit = audit;
            _notify = notify;
            _uploads = uploads;
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
                return (null, "Your account is not linked to a valid brokerage house. Please contact the AIPG team.", rejected);
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

            issue.StatusSince = issue.TDate;
            _sla.Start(issue); // the two service targets, counted from now

            _context.Issues.Add(issue);
            await _context.SaveChangesAsync(); // the ticket gets its id here

            // what used to be the "Comments" field of the form opens the conversation
            if (!string.IsNullOrWhiteSpace(form.Commands))
            {
                _context.TicketMessages.Add(NewMessage(issue, user, _actorRole, System.Net.WebUtility.HtmlEncode(form.Commands.Trim()).Replace("\n", "<br>"), isInternal: false, issue.TDate));
            }

            Log(AuditActions.TicketCreate, issue, "Raised the ticket \u201c" + issue.ITitle + "\u201d, priority " + issue.Priority);
            _notify.TicketRaised(issue, user);
            AutoAssign(issue); // the engineer of the product, or whoever the rule for new tickets names
            await _context.SaveChangesAsync();

            rejected = await SaveAttachmentsAsync(issue.IssueId, files);
            return (issue, null, rejected);
        }

        /// <summary>The id when it is a product tickets can be raised for (it exists and is active), else null.</summary>
        private int? ActiveProduct(int? id)
        {
            return id.HasValue && _context.Products.Any(product => product.ProductId == id && product.IsActive) ? id : null;
        }

        /// <summary>
        /// Stores uploaded files for a ticket. Returns the files that were refused, each with the reason in brackets:
        /// "setup.exe (file type not allowed)", "scan.pdf (larger than 10 MB)", "report.pdf (not a PDF file)".
        /// </summary>
        /// <param name="messageId">The conversation entry the files come with; null for files of the ticket itself.</param>
        /// <param name="internalNote">The entry is an internal note: its files are as internal as the note.</param>
        public async Task<List<string>> SaveAttachmentsAsync(int issueId, IEnumerable<IFormFile>? files, long? messageId = null, bool internalNote = false)
        {
            var rejected = new List<string>();
            if (files == null)
            {
                return rejected;
            }

            Directory.CreateDirectory(UploadFolder);
            var added = false;
            var names = new List<string>();
            var taken = 0;

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
                    rejected.Add((string.IsNullOrWhiteSpace(originalName) ? "(unnamed file)" : originalName) + " (file type not allowed)");
                    continue;
                }

                if (file.Length > _uploads.MaxFileBytes)
                {
                    rejected.Add(originalName + " (larger than " + _uploads.MaxFileMb + " MB)");
                    continue;
                }

                if (taken >= _uploads.MaxFilesPerSave)
                {
                    rejected.Add(originalName + " (more than " + _uploads.MaxFilesPerSave + " files at once)");
                    continue;
                }

                // the name says what the file claims to be; the first bytes say what it is
                string? contentProblem;
                await using (var content = file.OpenReadStream())
                {
                    contentProblem = await UploadContent.ProblemAsync(content, extension);
                }

                if (contentProblem != null)
                {
                    rejected.Add(originalName + " (" + contentProblem + ")");
                    continue;
                }

                taken++;

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
                    IssueId = issueId,
                    MessageId = messageId
                });
                names.Add(originalName);
                added = true;
            }

            if (added)
            {
                var issue = await _context.Issues.FirstOrDefaultAsync(i => i.IssueId == issueId);
                if (issue != null)
                {
                    if (internalNote)
                    {
                        // like the note itself: no brokerage house on the line, and left out of the history the house reads
                        _audit.Add(AuditActions.TicketNote, _actor, Rbac.Label(_actorRole), null, "Ticket", issue.IssueId, issue.TNumber, "Attached " + string.Join(", ", names) + " to an internal note");
                    }
                    else
                    {
                        Log(AuditActions.TicketFileAdd, issue, "Attached " + string.Join(", ", names));
                    }

                    // files that come with a new ticket are part of "ticket raised", files of a reply part of that reply
                    if (messageId == null && issue.TDate < DateTime.Now.AddMinutes(-1))
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

        /// <summary>Support engineers a ticket can be given to: AIPG staff with that position whose account works.</summary>
        public IQueryable<User> Engineers()
        {
            return _context.Users.Where(u => u.UType && !u.UCatagory && u.Department == Rbac.EngineerPosition && u.UStatus && u.VerifiedAt != null);
        }

        /// <summary>
        /// May the acting user set status and comments on this ticket? AIPG staff only: with "work on any ticket"
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
                    var statusBefore = issue.IStatus;
                    issue.IStatus = TicketStatus.Unassigned; // a ticket without an engineer is "Unassigned", whatever it was
                    Stamp(issue, statusBefore, now);
                    _sla.StatusChanged(issue, statusBefore, Rbac.IsStaff(_actorRole), now);
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
                    issue.ApproveBy = ActorName;
                }

                if (!closed && !TicketStatus.Work.Contains(TicketStatus.Normalize(issue.IStatus)))
                {
                    var statusBefore = issue.IStatus;
                    issue.IStatus = TicketStatus.Assigned;
                    Stamp(issue, statusBefore, now);
                }
            }

            if (same)
            {
                return null;
            }

            issue.UpdatedOn = now;
            issue.UpdatedBy = ActorName;

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
        /// Edit rules for AIPG staff: text fields, status, and (for roles that may assign) the engineer.
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
            Stamp(issue, oldStatus, now);
            if (newStatus == TicketStatus.Closed)
            {
                issue.ClosedOn = now;
                issue.ClosedBy = ActorName;
            }
            else
            {
                issue.ClosedOn = null;
                issue.ClosedBy = null;
            }

            issue.UpdatedOn = now;
            issue.UpdatedBy = ActorName;
            _sla.StatusChanged(issue, oldStatus, Rbac.IsStaff(_actorRole), now);
            Log(AuditActions.TicketStatus, issue,
                (newStatus == TicketStatus.Closed ? "Closed the ticket" : wasClosed ? "Reopened the ticket as " + TicketStatus.Name(newStatus) : "Status " + TicketStatus.Name(newStatus))
                + " (was " + TicketStatus.Name(oldStatus) + ")");
            _notify.TicketStatusChanged(issue, oldStatus, _actor);
            return null;
        }

        private string? WhyNot(IssueTable issue, string oldStatus, string newStatus)
        {
            var name = TicketStatus.Name(newStatus);
            if (_systemName == null && (TicketStatus.NeedsClosePermission(newStatus) || oldStatus == TicketStatus.Closed) && !Rbac.Can(_actorRole, Permission.TicketClose))
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

        private List<string> ApplyCommonFields(IssueTable issue, MakerView form, User editor)
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

            // (The "Comments" field is gone from the form: comments are entries of the conversation now, see AddMessageAsync.)

            if (!string.IsNullOrWhiteSpace(form.Priority) && form.Priority != issue.Priority && Array.IndexOf(Priorities, form.Priority) >= 0)
            {
                changes.Add("the priority from " + issue.Priority + " to " + form.Priority);
                issue.Priority = form.Priority;
                _sla.PriorityChanged(issue); // another priority has other service targets
            }

            if (changes.Count > 0)
            {
                issue.UpdatedOn = DateTime.Now;
                issue.UpdatedBy = editor.FullName;
            }

            return changes;
        }

        public static readonly string[] Priorities = { "Low", "Medium", "High" };

        // ---- conversation ----------------------------------------------------------------------

        /// <summary>
        /// The conversation of a ticket as the acting user may read it, oldest first: AIPG staff see everything,
        /// people of the brokerage house never see the internal notes.
        /// </summary>
        public async Task<List<XFLCSMS.Models.Desk.TicketMessage>> MessagesAsync(int issueId)
        {
            var staff = Rbac.IsStaff(_actorRole);
            return await _context.TicketMessages
                .Where(message => message.IssueId == issueId && (staff || !message.IsInternal))
                .OrderBy(message => message.Id)
                .ToListAsync();
        }

        /// <summary>May the acting user read this conversation entry (and download what came with it)?</summary>
        public bool CanRead(XFLCSMS.Models.Desk.TicketMessage message)
        {
            return !message.IsInternal || Rbac.IsStaff(_actorRole);
        }

        private static XFLCSMS.Models.Desk.TicketMessage NewMessage(IssueTable issue, User author, Role role, string html, bool isInternal, DateTime at)
        {
            return new XFLCSMS.Models.Desk.TicketMessage
            {
                IssueId = issue.IssueId,
                UserId = author.Id,
                AuthorName = author.FullName,
                AuthorRole = Rbac.Label(role),
                FromStaff = Rbac.IsStaff(role),
                IsInternal = isInternal,
                At = at,
                Body = html
            };
        }

        /// <summary>
        /// Adds a reply (or, from AIPG staff, an internal note) to the conversation, with its files. Everybody who may
        /// open the ticket may write; the caller has checked that. A reply of staff that the house can read counts as
        /// the first response. Returns the entry, or the reason nothing was added, and the files that were refused.
        /// </summary>
        public async Task<(XFLCSMS.Models.Desk.TicketMessage? Message, string? Error, List<string> RejectedFiles)> AddMessageAsync(
            IssueTable issue, string? html, bool isInternal, IEnumerable<IFormFile>? files)
        {
            var rejected = new List<string>();
            if (_actor == null)
            {
                return (null, "Please sign in again.", rejected);
            }

            var staff = Rbac.IsStaff(_actorRole);
            isInternal = isInternal && staff; // only AIPG staff write internal notes, whatever the form says
            var body = CleanRichText(html);
            var hasFiles = files != null && files.Any(file => file != null && file.Length > 0);
            if (string.IsNullOrWhiteSpace(PlainText(body)) && !hasFiles)
            {
                return (null, "Write a reply or attach a file first.", rejected);
            }

            if (body != null && body.Length > 200_000)
            {
                return (null, "The reply is too long. Put long texts into a file and attach it.", rejected);
            }

            var now = DateTime.Now;
            var message = NewMessage(issue, _actor, _actorRole, body ?? string.Empty, isInternal, now);
            _context.TicketMessages.Add(message);

            if (staff && !isInternal)
            {
                _sla.Responded(issue, now);
            }

            var preview = Preview(body, hasFiles);
            if (isInternal)
            {
                // The line carries no brokerage house: the activity list of a house shows the lines of that house, and
                // the house must not learn that AIPG made a note. (The history on the ticket page leaves it out too.)
                _audit.Add(AuditActions.TicketNote, _actor, Rbac.Label(_actorRole), null, "Ticket", issue.IssueId, issue.TNumber, "Added an internal note");
            }
            else
            {
                Log(AuditActions.TicketReply, issue, "Replied in the conversation");
            }
            if (isInternal)
            {
                _notify.TicketNoted(issue, _actor, preview);
            }
            else
            {
                _notify.TicketReplied(issue, _actor, staff, preview);
            }

            await _context.SaveChangesAsync(); // the entry gets its id here

            rejected = await SaveAttachmentsAsync(issue.IssueId, files, message.Id, isInternal);
            return (message, null, rejected);
        }

        /// <summary>The words of a formatted text, without the formatting.</summary>
        public static string PlainText(string? html)
        {
            if (string.IsNullOrEmpty(html))
            {
                return string.Empty;
            }

            var text = System.Text.RegularExpressions.Regex.Replace(html, "<(br|/p|/div|/li|/h[1-6])[^>]*>", " ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            text = System.Text.RegularExpressions.Regex.Replace(text, "<[^>]+>", string.Empty);
            return System.Text.RegularExpressions.Regex.Replace(System.Net.WebUtility.HtmlDecode(text), "\\s+", " ").Trim();
        }

        private static string Preview(string? html, bool hasFiles)
        {
            var text = PlainText(html);
            if (text.Length == 0)
            {
                return hasFiles ? "(attached a file)" : string.Empty;
            }

            return text.Length <= 160 ? text : text.Substring(0, 157) + "...";
        }

        // ---- rating ----------------------------------------------------------------------------

        /// <summary>May the acting user rate the support on this ticket? The person who raised it, once, after it was closed.</summary>
        public bool CanRate(IssueTable issue)
        {
            return _actor != null && issue.UserId == _actor.Id && issue.Rating == null && TicketStatus.IsClosed(issue.IStatus);
        }

        /// <summary>Stores the rating (1 to 5) with an optional sentence. Returns null, or why it was not stored.</summary>
        public string? Rate(IssueTable issue, int rating, string? comment)
        {
            if (!CanRate(issue))
            {
                return issue.Rating != null ? "This ticket was rated already." : "Only the person who raised a ticket can rate it, once it is closed.";
            }

            if (rating < 1 || rating > 5)
            {
                return "Choose between 1 and 5.";
            }

            comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
            if (comment != null && comment.Length > 1000)
            {
                comment = comment.Substring(0, 1000);
            }

            issue.Rating = rating;
            issue.RatingComment = comment;
            issue.RatedAt = DateTime.Now;
            Log(AuditActions.TicketRate, issue, "Rated the support " + rating + " of 5" + (comment == null ? string.Empty : ": \u201c" + comment + "\u201d"));
            _notify.TicketRated(issue, _actor!);
            return null;
        }

        // ---- audit -----------------------------------------------------------------------------

        private User? _actor;
        private Role _actorRole = Role.HouseUser;
        private string? _systemName;

        /// <summary>The name under which the rules of System &gt; Automation act.</summary>
        public const string AutomationName = "Automation";

        /// <summary>Tells the service who is acting in this request (set once by CsmsController).</summary>
        public void ActAs(User user, Role role)
        {
            _actor = user;
            _actorRole = role;
            _systemName = null;
        }

        /// <summary>
        /// Nobody is signed in: the system itself acts (a rule, a background check). The history names it by
        /// <paramref name="name"/>; it may set any status, as the people who wrote the rule may.
        /// </summary>
        public void ActAsSystem(string name)
        {
            _actor = null;
            _actorRole = Role.PlatformAdmin;
            _systemName = name;
        }

        /// <summary>Who is written into "closed by", "updated by", "assigned by".</summary>
        private string? ActorName => _actor?.FullName ?? _systemName;

        /// <summary>One line in the history of a ticket. It is saved with the next SaveChanges.</summary>
        public void Log(string action, IssueTable issue, string sentence)
        {
            _audit.Add(action, _actor, _systemName != null ? "System" : Rbac.Label(_actorRole), issue.BrokerageId, "Ticket", issue.IssueId, issue.TNumber, sentence, _systemName);
        }

        /// <summary>The status changed: since when it has the new one. Leaving "Pending" ends its reminders.</summary>
        private static void Stamp(IssueTable issue, string? statusBefore, DateTime now)
        {
            if (TicketStatus.Normalize(statusBefore) == TicketStatus.Normalize(issue.IStatus))
            {
                return;
            }

            issue.StatusSince = now;
            if (TicketStatus.Normalize(statusBefore) == TicketStatus.Pending)
            {
                issue.ReminderCount = 0;
                issue.LastReminderAt = null;
            }
        }

        // ---- automation ------------------------------------------------------------------------

        /// <summary>
        /// Gives a new ticket to an engineer when a rule says so: the default engineer of its product, otherwise
        /// what "new tickets" under System &gt; Automation names (in turn, or fewest open tickets). Recorded as done
        /// by "Automation", not by the person who raised the ticket. Returns the engineer, or null (nothing to do).
        /// </summary>
        public User? AutoAssign(IssueTable issue)
        {
            if (!IsUnassigned(issue) || TicketStatus.IsClosed(issue.IStatus))
            {
                return null;
            }

            var engineer = PickEngineer(issue);
            if (engineer == null)
            {
                return null;
            }

            var (actor, role, system) = (_actor, _actorRole, _systemName);
            ActAsSystem(AutomationName);
            try
            {
                Assign(issue, engineer);
            }
            finally
            {
                _actor = actor;
                _actorRole = role;
                _systemName = system;
            }

            return engineer;
        }

        private User? PickEngineer(IssueTable issue)
        {
            var mode = _rules.AssignMode;
            var productEngineer = issue.ProductId == null ? null
                : _context.Products.Where(product => product.ProductId == issue.ProductId).Select(product => product.EngineerId).FirstOrDefault();
            if (productEngineer == null && mode == AutomationRules.Off)
            {
                return null;
            }

            var engineers = Engineers().ToList();
            if (productEngineer != null)
            {
                // an engineer who left or was disabled no longer gets tickets: the general rule decides then
                var owner = engineers.FirstOrDefault(user => user.Id == productEngineer);
                if (owner != null) { return owner; }
            }

            if (mode == AutomationRules.Off || engineers.Count == 0)
            {
                return null;
            }

            // what each engineer holds: how many open tickets, and when the last one was assigned
            // (tickets assigned before the engineer's id was stored carry the name only: they count as well)
            var held = _context.Issues.Where(row => row.AssignedToId != null || (row.AssignBy != null && row.AssignBy != ""))
                .Select(row => new { row.AssignedToId, row.AssignBy, row.AssignOn, row.IStatus }).ToList();
            var ranked = engineers.Select(user =>
            {
                var mine = held.Where(row => row.AssignedToId == user.Id || (row.AssignedToId == null && row.AssignBy == user.FullName)).ToList();
                return new
                {
                    User = user,
                    Open = mine.Count(row => row.IStatus != TicketStatus.Closed),
                    Last = mine.Max(row => row.AssignOn) ?? DateTime.MinValue
                };
            }).ToList();

            return (mode == AutomationRules.LeastLoad
                    ? ranked.OrderBy(row => row.Open).ThenBy(row => row.Last).ThenBy(row => row.User.Id)
                    : ranked.OrderBy(row => row.Last).ThenBy(row => row.User.Id))
                .First().User;
        }

        /// <summary>
        /// An entry in the conversation that nobody typed: the system explains what it did ("closed automatically ...").
        /// The brokerage house reads it like a reply of AIPG.
        /// </summary>
        public void AddSystemMessage(IssueTable issue, string html, DateTime at)
        {
            _context.TicketMessages.Add(new XFLCSMS.Models.Desk.TicketMessage
            {
                IssueId = issue.IssueId,
                UserId = null,
                AuthorName = Infrastructure.Ui.Brand,
                AuthorRole = "automatic",
                FromStaff = true,
                IsInternal = false,
                At = at,
                Body = HtmlSanitizer.Sanitize(html)
            });
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
