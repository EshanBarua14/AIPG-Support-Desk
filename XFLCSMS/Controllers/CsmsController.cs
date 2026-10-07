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
    /// Shared behaviour of the five role controllers (Admin, SupportManegar, SupportEngineer, HouseAdmin, Maker):
    /// the session and permission check, which tickets a role sees, profile, change password, attachments,
    /// logout and error handling. The other parts of this class:
    ///   CsmsController.Assignment.cs  assign / take / release a ticket, workload
    ///   CsmsController.Users.cs       accounts (platform admin: all, house admin: own house)
    ///   CsmsController.Audit.cs       audit trail
    ///   CsmsController.Tickets.cs     ticket lists, board, report, view / edit / status / delete of a ticket
    ///   CsmsController.MasterData.cs  brokerage houses, branches, support lists
    ///   CsmsController.TeamTodos.cs   to-dos of all users
    ///   CsmsController.Notifications.cs  the bell: live stream, list, preferences
    ///   CsmsController.System.cs      system health, roles and permissions, notification settings, demo data
    /// </summary>
    public abstract partial class CsmsController : Controller
    {
        protected readonly DataContext Db;
        protected readonly TicketService Tickets;
        private User? _currentUser;

        protected CsmsController(DataContext context, TicketService tickets)
        {
            Db = context;
            Tickets = tickets;
        }

        /// <summary>The role this controller serves. Everything a role may do follows from it (Infrastructure/Rbac.cs).</summary>
        protected abstract Role MyRole { get; }

        /// <summary>Session entry of that role, e.g. "AdminData".</summary>
        protected string SessionKey => Rbac.SessionKey(MyRole);

        protected bool Can(Permission permission) => Rbac.Can(MyRole, permission);

        /// <summary>Writes the audit trail (Services/AuditService.cs).</summary>
        protected AuditService AuditTrailLog => HttpContext.RequestServices.GetRequiredService<AuditService>();

        /// <summary>One line in the audit trail, done by the signed-in user. It is saved with the next SaveChanges.</summary>
        protected void Audit(string action, string? entityType, int? entityId, string? label, string? details, int? houseId)
        {
            AuditTrailLog.Add(action, CurrentUser, Rbac.Label(MyRole), houseId, entityType, entityId, label, details);
        }

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
                Hub.Forget(HttpContext.Session.Id);
                context.Result = SessionAuthorizeAttribute.Challenge(Request);
                return;
            }

            // The session holds a copy of the user from sign-in. Compare it with the database on every request, so that
            // "Disabled", a role change, a deleted account or a new password works at once. (The session time-out restarts
            // with every click, so a disabled user who kept working was never signed out.)
            var stored = Db.Users
                .Where(u => u.Id == user.Id)
                .Select(u => new { u.UStatus, u.UCatagory, u.UType, u.Department, u.PasswordHash, u.BrokerageHouseName })
                .FirstOrDefault();
            if (stored == null
                || !stored.UStatus
                || Rbac.RoleOf(stored.UCatagory, stored.UType, stored.Department) != MyRole
                || stored.BrokerageHouseName != user.BrokerageHouseName // the house decides what a house admin / house user sees
                || SessionAuthorizeAttribute.StampOf(stored.PasswordHash) != HttpContext.Session.GetString(SessionAuthorizeAttribute.Stamp))
            {
                HttpContext.Session.Clear();
                if (!SessionAuthorizeAttribute.IsAjax(Request))
                {
                    TempData["Notice"] = stored != null && !stored.UStatus
                        ? "Your account is disabled. Please contact the XFL team."
                        : "Your account was changed. Please sign in again.";
                }

                context.Result = SessionAuthorizeAttribute.Challenge(Request);
                return;
            }

            // An action that needs a permission this role does not hold is refused here, before it runs.
            var needed = (context.ActionDescriptor as Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor)?
                .MethodInfo.GetCustomAttributes(typeof(RequirePermissionAttribute), true).OfType<RequirePermissionAttribute>().FirstOrDefault();
            if (needed != null && !needed.AnyOf.Any(Can))
            {
                context.Result = Refused();
                return;
            }

            // The notification stream of an open page is a request too, and every request keeps a session alive. So
            // the sign-in ends by the clock kept in NotificationHub: ten minutes after the user last did something
            // himself, whatever the open pages have been asking for in the meantime.
            var action = (context.ActionDescriptor as Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor)?.ActionName;
            var isStream = action == nameof(NotificationStream);
            var sessionId = HttpContext.Session.Id;
            if (isStream ? Hub.IsIdle(sessionId, SessionTimeout) : Hub.HasExpired(sessionId, SessionTimeout))
            {
                Hub.Forget(sessionId);
                HttpContext.Session.Clear();
                if (isStream)
                {
                    context.Result = new StatusCodeResult(StatusCodes.Status401Unauthorized);
                    return;
                }

                if (!SessionAuthorizeAttribute.IsAjax(Request))
                {
                    TempData["Notice"] = "You were signed out because nothing happened for a while. Please sign in again.";
                }

                context.Result = SessionAuthorizeAttribute.Challenge(Request);
                return;
            }

            if (isStream)
            {
                // the stream gets nothing a page needs, and does not count as the user doing something
                Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
                return;
            }

            Hub.Clicked(sessionId);

            // The layout prints ViewBag.Profile.FullName, so it must be set for every view.
            ViewBag.Profile = user;
            Tickets.ActAs(user, MyRole);
            Db.BeforeSaving = () => AuditTrailLog.AddMasterDataChanges(CurrentUser, Rbac.Label(MyRole));

            // "Assigned to" choices, loaded only when a page really shows them.
            ViewBag.EngineerChoices = new Lazy<List<EngineerLoad>>(LoadEngineerChoices);

            // Signed-in pages must not come back from the browser cache after "Log out" + Back.
            // (The old pages tried to do this with a script that pushed the history forward.)
            Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
            Response.Headers["Pragma"] = "no-cache";

            base.OnActionExecuting(context);
        }

        /// <summary>
        /// Runs after the action: when a whole page is about to be rendered (also the page a POST shows again
        /// with its validation messages), it gets the numbers for the menu and the bell.
        /// </summary>
        public override void OnActionExecuted(ActionExecutedContext context)
        {
            if (context.Result is ViewResult && !SessionAuthorizeAttribute.IsAjax(Request) && CurrentUser != null)
            {
                SetMenuCounters(CurrentUser);
            }

            base.OnActionExecuted(context);
        }

        /// <summary>The small numbers in the side menu (tickets per status, assigned to me, users waiting) and on the bell.</summary>
        private void SetMenuCounters(User user)
        {
            try
            {
                // tickets per status, for the sub menu "By status"
                var perStatus = VisibleIssues.Where(TicketService.NotClosed).GroupBy(i => i.IStatus).Select(g => new { Status = g.Key, Count = g.Count() }).ToList();
                var counts = new Dictionary<string, int>();
                foreach (var row in perStatus)
                {
                    var key = TicketStatus.Normalize(row.Status);
                    if (key != null) { counts[key] = counts.GetValueOrDefault(key) + row.Count; }
                }

                ViewBag.NavStatusCounts = counts;
                ViewBag.UnassignedCount = counts.GetValueOrDefault(TicketStatus.Unassigned);
                if (MyRole == Role.SupportEngineer)
                {
                    ViewBag.AssignedToMeCount = Db.Issues.Where(TicketService.AssignedTo(user)).Count(TicketService.NotClosed);
                }
                ViewBag.UnreadNotifications = Db.Notifications.Count(n => n.UserId == user.Id && n.ReadAt == null);
                ViewBag.InAppNotifications = Services.Notify.NotificationEvents.ChannelOn(Settings, Services.Notify.NotificationEvents.InAppChannel);
                if (Can(Permission.UsersAll) || Can(Permission.UsersHouse))
                {
                    // registered, but the token from the e-mail was never entered: an administrator can activate them
                    ViewBag.PendingUsersCount = ManagedUsers.Count(u => u.VerifiedAt == null && u.UStatus);
                }
            }
            catch (Exception exception)
            {
                // A menu counter is never worth an error page.
                HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(GetType())
                    .LogWarning(exception, "Could not load the menu counters");
            }
        }

        /// <summary>The answer to a request this role is not allowed to make.</summary>
        protected IActionResult Refused()
        {
            if (SessionAuthorizeAttribute.IsAjax(Request))
            {
                return StatusCode(StatusCodes.Status403Forbidden, "You are not allowed to do this.");
            }

            return StatusCode(StatusCodes.Status403Forbidden); // shown as the "not allowed" page (Views/Shared/Status.cshtml)
        }

        /// <summary>
        /// Tickets this role may see. With "see all tickets": every ticket. With "see the tickets of the own house":
        /// those. Otherwise the tickets the user raised and - for XFL staff - the ones assigned to him.
        /// Every list, report and counter starts here.
        /// </summary>
        protected IQueryable<IssueTable> VisibleIssues
        {
            get
            {
                if (Can(Permission.TicketsAll))
                {
                    return Db.Issues;
                }

                var me = CurrentUser;
                var myId = me?.Id ?? 0;
                if (Can(Permission.TicketsHouse))
                {
                    var myHouse = me?.BrokerageHouseName ?? 0;
                    return Db.Issues.Where(i => i.BrokerageId == myHouse);
                }

                if (Rbac.IsStaff(MyRole))
                {
                    // XFL staff without "see all tickets": the tickets assigned to them and the ones they raised
                    var myName = me?.FullName;
                    return Db.Issues.Where(i => i.UserId == myId || i.AssignedToId == myId || (i.AssignedToId == null && i.AssignBy == myName));
                }

                return Db.Issues.Where(i => i.UserId == myId);
            }
        }

        /// <summary>May this role open that ticket? The same rule as <see cref="VisibleIssues"/>, for one ticket.</summary>
        protected bool CanAccessIssue(IssueTable issue)
        {
            if (Can(Permission.TicketsAll))
            {
                return true;
            }

            var me = CurrentUser;
            if (me == null)
            {
                return false;
            }

            return (Can(Permission.TicketsHouse) && issue.BrokerageId == me.BrokerageHouseName)
                || issue.UserId == me.Id
                || (Rbac.IsStaff(MyRole) && TicketService.IsAssignedTo(issue, me));
        }

        /// <summary>
        /// Null when the signed-in user may change this ticket; otherwise the way back to the ticket with the reason.
        /// (Seeing a ticket is not the same as being allowed to change it: see TicketService.CanEdit.)
        /// </summary>
        protected IActionResult? RefuseEdit(IssueTable issue)
        {
            if (Tickets.CanEdit(issue))
            {
                return null;
            }

            if (Rbac.IsStaff(MyRole))
            {
                TempData["ErrorMessage"] = TicketService.IsUnassigned(issue)
                    ? (Can(Permission.TicketTake)
                        ? "Take ticket " + issue.TNumber + " first: you work on the tickets that are assigned to you."
                        : "Ticket " + issue.TNumber + " has no engineer yet. Your role works on the tickets assigned to it.")
                    : "Ticket " + issue.TNumber + " is assigned to " + issue.AssignBy + ". Your role works on the tickets assigned to it.";
            }
            else if (TicketStatus.IsClosed(issue.IStatus))
            {
                TempData["ErrorMessage"] = "Ticket " + issue.TNumber + " is closed and can no longer be changed.";
            }
            else
            {
                TempData["ErrorMessage"] = "Ticket " + issue.TNumber + " was raised by a colleague. You can change only the tickets you raised yourself.";
            }

            return RedirectToAction("TicketView", new { id = issue.IssueId });
        }

        /// <summary>Lists that show tickets of several people (house admin) print who raised each ticket.</summary>
        protected void SetRaisers(IEnumerable<IssueTable> tickets)
        {
            if (!Can(Permission.TicketsHouse))
            {
                return;
            }

            var ids = tickets.Select(t => t.UserId).Distinct().ToList();
            ViewBag.Raisers = Db.Users.Where(u => ids.Contains(u.Id)).ToDictionary(u => u.Id, u => u.FullName);
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
                .Select(i => new { i.IssueId, i.BrokerageId, i.IStatus, i.TDate, i.AssignBy, i.AssignOn, i.AssignedToId, i.Priority })
                .ToListAsync();

            var today = DateTime.Now.Date;
            int Raised(DateTime from) => rows.Count(r => r.TDate.Date >= from && r.TDate.Date <= today);
            int Closed(DateTime from) => rows.Count(r => r.TDate.Date >= from && r.TDate.Date <= today && r.IStatus == TicketStatus.Closed);

            var week = today.AddDays(-7);
            var month = today.AddMonths(-1);
            var year = today.AddYears(-1);

            var total = rows.Count;
            var closed = rows.Count(r => r.IStatus == TicketStatus.Closed);

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
                Unassigned = rows.Count(r => r.AssignedToId == null && string.IsNullOrEmpty(r.AssignBy) && r.IStatus != TicketStatus.Closed),
                HighPriorityOpen = rows.Count(r => r.Priority == "High" && r.IStatus != TicketStatus.Closed),
                AssignedToMe = rows.Count(r => (r.AssignedToId == me.Id || (r.AssignedToId == null && r.AssignBy == me.FullName)) && r.IStatus != TicketStatus.Closed)
            };

            foreach (var status in TicketStatus.All)
            {
                board.ByStatus[status.Key] = rows.Count(r => TicketStatus.Normalize(r.IStatus) == status.Key);
            }

            if (includeHouses)
            {
                var houses = await Db.Brokerages.ToListAsync();
                board.Houses = houses
                    .Select(h => new HouseLoad
                    {
                        Name = h.BrokerageHouseName,
                        Acronym = h.BrokerageHouseAcronym,
                        Open = rows.Count(r => r.BrokerageId == h.BrokerageId && r.IStatus != TicketStatus.Closed),
                        Closed = rows.Count(r => r.BrokerageId == h.BrokerageId && r.IStatus == TicketStatus.Closed)
                    })
                    .Where(h => h.Total > 0)
                    .OrderByDescending(h => h.Total)
                    .ThenBy(h => h.Name)
                    .ToList();
            }

            var recentIds = rows.OrderByDescending(r => r.IssueId).Take(6).Select(r => r.IssueId).ToList();
            var waitingIds = rows
                .Where(r => r.AssignedToId == null && string.IsNullOrEmpty(r.AssignBy) && r.IStatus != TicketStatus.Closed)
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
                AssignedToId = issue.AssignedToId,
                CanEdit = Tickets.CanEdit(issue),
                CanWork = Tickets.CanWorkOn(issue),
                StatusChoices = Tickets.StatusChoices(issue),
                IsMine = CurrentUser != null && TicketService.IsAssignedTo(issue, CurrentUser),
                History = Db.AuditLogs.Where(line => line.EntityType == "Ticket" && line.EntityId == issue.IssueId).OrderBy(line => line.Id).ToList(),
                TicketDetails = issue.Details,
                Command = issue.Comments,
                TicketStatus = issue.IStatus,
                IStatus = issue.IStatus,
                Priority = issue.Priority,
                IssueTitle = issue.ITitle,
                Attachments = issue.attachment ?? new List<Attachment>(),
                SupportEngineers = includeEngineers
                    ? Tickets.Engineers().OrderBy(u => u.FullName).ToList()
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
            if (CurrentUser != null)
            {
                Audit(AuditActions.SignOut, "User", CurrentUser.Id, CurrentUser.FullName + " (" + CurrentUser.UserName + ")", null, Rbac.HouseOf(CurrentUser));
                Db.SaveChanges();
            }

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
                Audit(AuditActions.PasswordChange, "User", user.Id, user.FullName + " (" + user.UserName + ")", "Changed their own password", Rbac.HouseOf(user));
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

            // seeing a ticket is not enough to remove its files: the same rule as for editing it
            if (!Tickets.CanEdit(attachment.issue))
            {
                return Refused();
            }

            await Tickets.DeleteAttachmentAsync(attachment);
            return Ok();
        }
    }
}
