using Microsoft.AspNetCore.StaticFiles;
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

        public TicketService(DataContext context, IWebHostEnvironment environment)
        {
            _context = context;
            _environment = environment;
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
                Details = form.IssueDetails,
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

            _context.Issues.Add(issue);
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
                added = true;
            }

            if (added)
            {
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
        public async Task DeleteAttachmentAsync(Attachment attachment)
        {
            var path = ResolveAttachmentPath(attachment);
            var location = attachment.AttachmentLoc;

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

        /// <summary>Edit rules for the ticket owner (Maker): text fields and priority only.</summary>
        public void ApplyOwnerEdit(IssueTable issue, MakerView form, User editor)
        {
            ApplyCommonFields(issue, form, editor);
        }

        /// <summary>Edit rules for XFL staff (Admin / Support Manager / Support Engineer): also assignment and status.</summary>
        public void ApplyStaffEdit(IssueTable issue, MakerView form, User editor, bool canApprove)
        {
            ApplyCommonFields(issue, form, editor);

            var now = DateTime.Now;
            var newAssignee = string.IsNullOrWhiteSpace(form.AssgnBy) ? null : form.AssgnBy;
            var assigneeChanged = !string.Equals(newAssignee, string.IsNullOrWhiteSpace(issue.AssignBy) ? null : issue.AssignBy, StringComparison.Ordinal);

            if (newAssignee == null)
            {
                issue.AssignBy = null;
                issue.AssignOn = null;
                issue.ApproveBy = null;
                issue.ApproveOn = null;
            }
            else
            {
                if (assigneeChanged || issue.AssignOn == null)
                {
                    // Stamp the assignment time once, when the engineer is actually (re)assigned.
                    issue.AssignBy = newAssignee;
                    issue.AssignOn = now;
                }

                if (canApprove && (assigneeChanged || issue.ApproveOn == null))
                {
                    issue.ApproveOn = now;
                    issue.ApproveBy = editor.FullName;
                }
            }

            // A missing status (field not posted) keeps the current one instead of blanking it.
            var newStatus = string.IsNullOrWhiteSpace(form.IStatus) ? issue.IStatus : form.IStatus;
            var wasClosed = issue.IStatus == "Close";
            issue.IStatus = newStatus;

            if (newStatus == "Close")
            {
                if (!wasClosed || issue.ClosedOn == null)
                {
                    issue.ClosedOn = now;
                    issue.ClosedBy = editor.FullName;
                }
            }
            else
            {
                issue.ClosedOn = null;
                issue.ClosedBy = null;
            }
        }

        private static void ApplyCommonFields(IssueTable issue, MakerView form, User editor)
        {
            if (!string.IsNullOrWhiteSpace(form.IssueTitle))
            {
                issue.ITitle = form.IssueTitle.Trim();
            }

            issue.Details = form.TicketDetails;
            issue.Comments = form.Command;

            if (!string.IsNullOrWhiteSpace(form.Priority))
            {
                issue.Priority = form.Priority;
            }

            issue.UpdatedOn = DateTime.Now;
            issue.UpdatedBy = editor.FullName;
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
