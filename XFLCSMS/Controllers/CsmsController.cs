using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Newtonsoft.Json;
using XFLCSMS.Infrastructure;
using XFLCSMS.Models.Admin;
using Microsoft.AspNetCore.Http;
using XFLCSMS.Models.Issue;
using XFLCSMS.Models.Register;
using XFLCSMS.Services;

namespace XFLCSMS.Controllers
{
    /// <summary>
    /// Shared behaviour of the four role controllers (Admin, SupportManegar, SupportEngineer, Maker):
    /// the session check, profile, change password, attachments, logout and error handling.
    /// </summary>
    public abstract class CsmsController : Controller
    {
        protected readonly DataContext Db;
        protected readonly TicketService Tickets;
        private User? _currentUser;

        protected CsmsController(DataContext context, TicketService tickets)
        {
            Db = context;
            Tickets = tickets;
        }

        /// <summary>Session key of the role this controller serves, e.g. "AdminData".</summary>
        protected abstract string SessionKey { get; }

        /// <summary>The signed-in user of this role, or null when the session is missing or expired.</summary>
        protected User? CurrentUser
        {
            get
            {
                if (_currentUser == null)
                {
                    var json = HttpContext.Session.GetString(SessionKey);
                    if (!string.IsNullOrEmpty(json))
                    {
                        _currentUser = JsonConvert.DeserializeObject<User>(json);
                    }
                }

                return _currentUser;
            }
        }

        /// <summary>Runs before every action: no valid session means back to the login page.</summary>
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            var user = CurrentUser;
            if (user == null)
            {
                context.Result = SessionAuthorizeAttribute.Challenge(Request);
                return;
            }

            // The layout prints ViewBag.Profile.FullName, so it must be set for every view.
            ViewBag.Profile = user;

            // Signed-in pages must not come back from the browser cache after "Log out" + Back.
            // (The old pages tried to do this with a script that pushed the history forward.)
            Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
            Response.Headers["Pragma"] = "no-cache";

            if (HttpMethods.IsGet(Request.Method) && !SessionAuthorizeAttribute.IsAjax(Request))
            {
                SetMenuCounters(user);
            }

            base.OnActionExecuting(context);
        }

        /// <summary>The small numbers beside "Unassigned tickets" and "Assigned to me" in the side menu.</summary>
        private void SetMenuCounters(User user)
        {
            try
            {
                ViewBag.UnassignedCount = VisibleIssues.Count(i => i.AssignBy == null && i.AssignOn == null && i.IStatus != "Close");
                if (SessionKey == SessionAuthorizeAttribute.SupportEngineer)
                {
                    ViewBag.AssignedToMeCount = Db.Issues.Count(i => i.AssignBy == user.FullName && i.IStatus != "Close");
                }
            }
            catch (Exception exception)
            {
                // A menu counter is never worth an error page.
                HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(GetType())
                    .LogWarning(exception, "Could not load the menu counters");
            }
        }

        /// <summary>Tickets this role may see: everything for XFL staff, the user's own tickets for makers.</summary>
        protected virtual IQueryable<IssueTable> VisibleIssues => Db.Issues;

        /// <summary>Tickets this role may open. Makers are limited to their own tickets.</summary>
        protected virtual bool CanAccessIssue(IssueTable issue)
        {
            return true;
        }

        /// <summary>
        /// Used by the catch blocks. They used to sign the user out on ANY exception, which hid the real
        /// error and looked like a random logout. Now the error is logged and an error page is shown.
        /// </summary>
        protected IActionResult HandleError(Exception exception)
        {
            var logger = HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(GetType());
            logger.LogError(exception, "Unhandled error in {Path}", Request.Path);

            Response.StatusCode = StatusCodes.Status500InternalServerError;
            if (SessionAuthorizeAttribute.IsAjax(Request))
            {
                return Content("Something went wrong while processing the request.");
            }

            return View("Error", new ErrorViewModel { RequestId = HttpContext.TraceIdentifier });
        }

        /// <summary>
        /// Dashboard numbers for the tickets this role may see. One implementation for the four roles
        /// (each controller used to carry its own 100-line copy).
        /// </summary>
        protected async Task<Dashboard> BuildDashboardAsync(bool includeHouses)
        {
            var me = CurrentUser!;
            var rows = await VisibleIssues
                .Select(i => new { i.IssueId, i.BrokerageId, i.IStatus, i.TDate, i.AssignBy, i.AssignOn, i.Priority })
                .ToListAsync();

            var today = DateTime.Now.Date;
            int Raised(DateTime from) => rows.Count(r => r.TDate.Date >= from && r.TDate.Date <= today);
            int Closed(DateTime from) => rows.Count(r => r.TDate.Date >= from && r.TDate.Date <= today && r.IStatus == "Close");

            var week = today.AddDays(-7);
            var month = today.AddMonths(-1);
            var year = today.AddYears(-1);

            var total = rows.Count;
            var closed = rows.Count(r => r.IStatus == "Close");

            var board = new Dashboard
            {
                TotalTicket = total,
                TotalClosed = closed,
                TotalQueue = total - closed,
                TodayTotalTicket = Raised(today),
                TodayTotalClosed = Closed(today),
                TodayTotalQueue = Raised(today) - Closed(today),
                WeeklyTotalTicket = Raised(week),
                WeeklyTotalClosed = Closed(week),
                WeeklyTotalQueue = Raised(week) - Closed(week),
                MonthlyTotalTicket = Raised(month),
                MonthlyTotalClosed = Closed(month),
                MonthlyTotalQueue = Raised(month) - Closed(month),
                YearlyTotalTicket = Raised(year),
                YearlyTotalClosed = Closed(year),
                YearlyTotalQueue = Raised(year) - Closed(year),
                Unassigned = rows.Count(r => r.AssignBy == null && r.AssignOn == null && r.IStatus != "Close"),
                HighPriorityOpen = rows.Count(r => r.Priority == "High" && r.IStatus != "Close"),
                AssignedToMe = rows.Count(r => r.AssignBy == me.FullName && r.IStatus != "Close")
            };

            if (includeHouses)
            {
                var houses = await Db.Brokerages.ToListAsync();
                board.Houses = houses
                    .Select(h => new HouseLoad
                    {
                        Name = h.BrokerageHouseName,
                        Acronym = h.BrokerageHouseAcronym,
                        Open = rows.Count(r => r.BrokerageId == h.BrokerageId && r.IStatus != "Close"),
                        Closed = rows.Count(r => r.BrokerageId == h.BrokerageId && r.IStatus == "Close")
                    })
                    .Where(h => h.Total > 0)
                    .OrderByDescending(h => h.Total)
                    .ThenBy(h => h.Name)
                    .ToList();
            }

            var recentIds = rows.OrderByDescending(r => r.IssueId).Take(6).Select(r => r.IssueId).ToList();
            var waitingIds = rows
                .Where(r => r.AssignBy == null && r.AssignOn == null && r.IStatus != "Close")
                .OrderBy(r => r.TDate)
                .Take(6)
                .Select(r => r.IssueId)
                .ToList();

            var wanted = recentIds.Concat(waitingIds).Distinct().ToList();
            var tickets = wanted.Count == 0
                ? new List<IssueTable>()
                : await Db.Issues.Where(i => wanted.Contains(i.IssueId)).ToListAsync();

            board.Recent = recentIds.Select(id => tickets.FirstOrDefault(t => t.IssueId == id)).Where(t => t != null).Select(t => t!).ToList();
            board.Waiting = waitingIds.Select(id => tickets.FirstOrDefault(t => t.IssueId == id)).Where(t => t != null).Select(t => t!).ToList();
            return board;
        }

        /// <summary>The ticket as shown on the view and edit pages (names instead of ids, attachments, ...).</summary>
        protected MakerView ToMakerView(IssueTable issue, bool includeEngineers)
        {
            return new MakerView
            {
                IssueId = issue.IssueId,
                TicketNumber = issue.TNumber,
                BrokerageHouse = Db.Brokerages.Where(b => b.BrokerageId == issue.BrokerageId).Select(b => b.BrokerageHouseName).FirstOrDefault(),
                CreatedOn = issue.TDate,
                CreatedBy = Db.Users.Where(u => u.Id == issue.UserId).Select(u => u.FullName).FirstOrDefault() ?? string.Empty,
                AssgnOn = issue.AssignOn,
                AssgnBy = issue.AssignBy,
                ApproveOn = issue.ApproveOn,
                ApproveBy = issue.ApproveBy,
                UpdatedOn = issue.UpdatedOn,
                UpdatedBy = issue.UpdatedBy,
                CloseOn = issue.ClosedOn,
                ClosedbyName = issue.ClosedBy,
                SupportType = Db.SupportTypes.Where(x => x.SupportTypeId == issue.SupportTypeId).Select(x => x.SType).FirstOrDefault(),
                SupportCatagory = Db.SupportCatagories.Where(x => x.SupportCatagoryId == issue.SupportCatagoryId).Select(x => x.SCatagory).FirstOrDefault(),
                SupportSubCatagory = Db.SupportSubCatagories.Where(x => x.SupportSubCatagoryId == issue.SupportSubCatagoryId).Select(x => x.SubCatagory).FirstOrDefault(),
                AffectedSection = Db.AffectedSectionss.Where(x => x.AffectedSectionId == issue.AffectedSectionId).Select(x => x.ASection).FirstOrDefault(),
                TicketDetails = issue.Details,
                Command = issue.Comments,
                TicketStatus = issue.IStatus,
                IStatus = issue.IStatus,
                Priority = issue.Priority,
                IssueTitle = issue.ITitle,
                Attachments = issue.attachment ?? new List<Attachment>(),
                SupportEngineers = includeEngineers
                    ? Db.Users.Where(u => u.Department == "Support Engineer").OrderBy(u => u.FullName).ToList()
                    : null
            };
        }

        /// <summary>
        /// Sign out. POST only (with the anti-forgery token): as a plain link, any page - or an image inside a
        /// ticket - could sign a user out just by being loaded.
        /// </summary>
        [HttpPost]
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Login", "RegisterLogin");
        }

        /// <summary>"My Profile" always shows the signed-in user; the id in the URL is ignored on purpose.</summary>
        public async Task<IActionResult> Profile(int id)
        {
            try
            {
                var me = CurrentUser!;
                var user = await Db.Users.FirstOrDefaultAsync(item => item.Id == me.Id);
                if (user == null)
                {
                    return NotFound();
                }

                var house = await Db.Brokerages.FirstOrDefaultAsync(b => b.BrokerageId == user.BrokerageHouseName);
                var branch = await Db.Branchhs.FirstOrDefaultAsync(b => b.BranchId == user.Branch);

                var userView = new UserView
                {
                    Id = user.Id,
                    FullName = user.FullName,
                    Email = user.Email,
                    PhonNumber = user.PhonNumber,
                    Designation = user.Designation ?? string.Empty,
                    Department = user.Department ?? string.Empty,
                    BrokerageHouseName = house?.BrokerageHouseName ?? string.Empty,
                    Branch = branch?.BranchName ?? string.Empty,
                    EmployeeId = user.EmployeeId,
                    UserName = user.UserName,
                    UCatagory = user.UCatagory,
                    UType = user.UType,
                    UStatus = user.UStatus
                };

                return View(userView);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [HttpGet]
        public IActionResult ChangePassword()
        {
            return View(new Password { UserId = CurrentUser!.Id });
        }

        [HttpPost]
        public async Task<IActionResult> ChangePassword(Password password)
        {
            try
            {
                // Always the signed-in user: the posted UserId is ignored so nobody can target another account.
                var me = CurrentUser!;
                password.UserId = me.Id;

                var user = await Db.Users.FirstOrDefaultAsync(i => i.Id == me.Id);
                if (user == null)
                {
                    return Logout();
                }

                if (!PasswordHasher.Verify(password.CurrentPassword, user.PasswordHash, user.PasswordSalt))
                {
                    ViewBag.message = "The current password is not correct.";
                    return View(password);
                }

                if (string.IsNullOrEmpty(password.NewPassword) || ModelState[nameof(Password.NewPassword)]?.Errors.Count > 0)
                {
                    ViewBag.message = "Password must contain at least one lowercase letter, one uppercase letter, one digit, and one special character";
                    return View(password);
                }

                if (password.NewPassword != password.ConNewPassword)
                {
                    ViewBag.message = "New password and confirm password are not the same.";
                    return View(password);
                }

                PasswordHasher.Create(password.NewPassword, out byte[] passwordHash, out byte[] passwordSalt);
                user.PasswordHash = passwordHash;
                user.PasswordSalt = passwordSalt;
                await Db.SaveChangesAsync();

                TempData["Message"] = "Your password was changed. Sign in with the new password.";
                return Logout();
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        public async Task<IActionResult> DownloadAttachment(int? att)
        {
            if (att == null)
            {
                return BadRequest("Attachment ID is missing in the query parameters.");
            }

            var attachment = await Db.Attachments.Include(a => a.issue).FirstOrDefaultAsync(i => i.AttachmentId == att);
            if (attachment == null || attachment.issue == null || !CanAccessIssue(attachment.issue))
            {
                return NotFound("Attachment not found.");
            }

            var filePath = Tickets.ResolveAttachmentPath(attachment);
            if (filePath == null)
            {
                return NotFound("File not found.");
            }

            return PhysicalFile(filePath, TicketService.GetContentType(attachment.FileName), attachment.FileName);
        }

        [HttpPost]
        public async Task<IActionResult> DeleteAttachment(int attachmentId)
        {
            var attachment = await Db.Attachments.Include(a => a.issue).FirstOrDefaultAsync(a => a.AttachmentId == attachmentId);
            if (attachment == null || attachment.issue == null || !CanAccessIssue(attachment.issue))
            {
                return NotFound();
            }

            await Tickets.DeleteAttachmentAsync(attachment);
            return Ok();
        }
    }
}
