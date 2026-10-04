using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Newtonsoft.Json;
using XFLCSMS.Infrastructure;
using XFLCSMS.Models.Admin;
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

            // Every layout prints ViewBag.Profile.FullName, so it must be set for every view.
            ViewBag.Profile = user;
            base.OnActionExecuting(context);
        }

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
