using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using XFLCSMS.Infrastructure;
using XFLCSMS.Models.Email;
using XFLCSMS.Models.Login;
using XFLCSMS.Models.Register;
using XFLCSMS.Services;
using Newtonsoft.Json;

namespace XFLCSMS.Controllers
{
    public class RegisterLoginController : Controller
    {
        private readonly DataContext _context;
        private readonly IEmailServices _emailServices;
        private readonly ILogger<RegisterLoginController> _logger;
        private readonly AuditService _audit;

        public RegisterLoginController(DataContext context, IEmailServices emailServices, ILogger<RegisterLoginController> logger, AuditService audit)
        {
            _context = context;
            _emailServices = emailServices;
            _logger = logger;
            _audit = audit;
        }

        /// <summary>One line in the audit trail about an account, done by somebody who is not signed in (yet).</summary>
        private void Audit(string action, User user, string? details, bool bySelf = true)
        {
            var label = user.FullName + " (" + user.UserName + ")";
            _audit.Add(action, bySelf ? user : null, bySelf ? Rbac.Label(Rbac.RoleOf(user)) : null, Rbac.HouseOf(user), "User", user.Id, label, details, bySelf ? null : "(not signed in)");
        }

        public IActionResult Index()
        {
            return RedirectToAction("Login");
        }

        public IActionResult Register()
        {
            var viewModel = new RegisterViewModel
            {
                Brokerages = _context.Brokerages.ToList(),
                userRegisterRequest = new UserRegisterRequest()
            };
            return View(viewModel);
        }

        public IActionResult Verify()
        {
            return View();
        }

        public IActionResult Login()
        {
            // Already signed in (for example after pressing "Start page"): go to the dashboard, not back to this form.
            foreach (var role in Rbac.AllRoles)
            {
                if (!string.IsNullOrEmpty(HttpContext.Session.GetString(Rbac.SessionKey(role))))
                {
                    return RedirectToAction("Dashbord", Rbac.Controller(role));
                }
            }

            return View();
        }

        public IActionResult ForgotPassword()
        {
            return View();
        }

        public IActionResult ResetPassword()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Register(RegisterViewModel registerView)
        {
            var request = registerView.userRegisterRequest ??= new UserRegisterRequest();

            if (ModelState.IsValid)
            {
                // The drop-downs post ids; make sure they point at real rows that belong together.
                if (!_context.Brokerages.Any(b => b.BrokerageId == request.BrokerageHouseName))
                {
                    ModelState.AddModelError("userRegisterRequest.BrokerageHouseName", "Please select your organization.");
                }
                else if (!_context.Branchhs.Any(b => b.BranchId == request.Branch && b.BrokerageId == request.BrokerageHouseName))
                {
                    ModelState.AddModelError("userRegisterRequest.Branch", "Please select your branch.");
                }

                // People sign in with user name OR email, so neither may collide with the other column.
                if (_context.Users.Any(u => u.Email == request.Email || u.UserName == request.Email))
                {
                    ModelState.AddModelError("userRegisterRequest.Email", "This email is already registered.");
                }

                if (_context.Users.Any(u => u.UserName == request.UserName || u.Email == request.UserName))
                {
                    ModelState.AddModelError("userRegisterRequest.UserName", "This user name is already taken.");
                }
            }

            if (!ModelState.IsValid)
            {
                return RegisterForm(registerView);
            }

            PasswordHasher.Create(request.Password, out byte[] passwordHash, out byte[] passwordSalt);

            var user = new User
            {
                Email = request.Email,
                PasswordHash = passwordHash,
                PasswordSalt = passwordSalt,
                VerificationToken = CreateRandomToken(),
                FullName = request.FullName,
                PhonNumber = request.PhonNumber,
                Designation = request.Designation,
                BrokerageHouseName = request.BrokerageHouseName,
                Branch = request.Branch,
                EmployeeId = request.EmployeeId,
                Terms = request.Terms,
                UserName = request.UserName,
                BrokerageHouseAcronym = request.BrokerageHouseName,
                UStatus = true,
                UCatagory = false,
                UType = false,
            };

            await _context.Users.AddAsync(user);
            await _context.SaveChangesAsync(); // the account gets its id here

            Audit(AuditActions.Register, user, "Registered with " + user.Email + "; waits for activation");
            // the administrators who can activate the account are told (bell, and e-mail if that is switched on)
            HttpContext.RequestServices.GetRequiredService<XFLCSMS.Services.Notify.NotificationService>().AccountWaiting(user);
            await _context.SaveChangesAsync();

            try
            {
                _emailServices.SendEmail(new EmailDto
                {
                    To = request.Email,
                    Subject = "Registration Token for XFL Support System Software",
                    Body = "Dear Concern,<br/>" +
                           "Thank you for registering in XFLCSMS.<br/><br/>" +
                           "Your Registration Token is: <b>" + user.VerificationToken + "</b><br/><br/>" +
                           "Please use this token to complete your registration process. If you did not request this token, " +
                           "please ignore this email.<br/><br/>" +
                           "If you encounter any issues or need assistance, feel free to contact us at " +
                           "info@xpertfintech.com.<br/><br/>Thank you,<br/>Xpert Fintech Limited"
                });
            }
            catch (Exception ex)
            {
                // The mail server is down or refuses us. The account stays, waiting for activation: an administrator
                // activates it under Users. (It used to be deleted, so nobody could register while mail was broken.)
                _logger.LogError(ex, "Could not send the registration email to {Email}", request.Email);

                TempData["Notice"] = "Your account is created, but we could not send the activation email. " +
                                      "Ask the XFL support team to activate your account; after that you can sign in.";
                return RedirectToAction("Login");
            }

            TempData["Message"] = "Registration successful. Enter the token we emailed to you to activate your account.";
            return RedirectToAction("Verify");
        }

        // Re-display the registration form with its drop-downs filled in again.
        private IActionResult RegisterForm(RegisterViewModel registerView)
        {
            var request = registerView.userRegisterRequest;
            registerView.Brokerages = _context.Brokerages.ToList();
            registerView.Acronyme = _context.Brokerages
                .Where(b => b.BrokerageId == request.BrokerageHouseName)
                .Select(b => b.BrokerageHouseAcronym)
                .FirstOrDefault();
            registerView.Branchhs = _context.Branchhs
                .Where(b => b.BrokerageId == request.BrokerageHouseName)
                .ToList();
            return View("Register", registerView);
        }

        [HttpPost]
        public async Task<IActionResult> Verify(Verify verify)
        {
            if (string.IsNullOrWhiteSpace(verify.Email) || string.IsNullOrWhiteSpace(verify.Token))
            {
                ViewBag.Message = "Please enter your email (or user name) and the token.";
                return View(verify);
            }

            var token = verify.Token.Trim();
            var login = verify.Email.Trim();
            var user = await _context.Users.FirstOrDefaultAsync(u => (u.VerificationToken == token) &&
                (u.Email == login || u.UserName == login));
            if (user == null)
            {
                ViewBag.Message = "Invalid token or email.";
                return View(verify);
            }

            if (user.VerifiedAt == null)
            {
                user.VerifiedAt = DateTime.Now;
                Audit(AuditActions.Verify, user, "Activated the account with the token from the e-mail");
                await _context.SaveChangesAsync();
            }

            TempData["Message"] = "Your account is verified. You can sign in now.";
            return RedirectToAction("Login");
        }

        [HttpPost]
        public async Task<IActionResult> Login(UserLoginRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.UserId) || string.IsNullOrEmpty(request.Password))
            {
                return LoginFailed(request, "Please enter your user name (or email) and password.");
            }

            var login = request.UserId.Trim();
            var user = await _context.Users.FirstOrDefaultAsync(u => (u.Email == login || u.UserName == login));

            // Same message for "no such user" and "wrong password" so the form does not reveal who is registered.
            if (user == null || !PasswordHasher.Verify(request.Password, user.PasswordHash, user.PasswordSalt))
            {
                if (user != null)
                {
                    // only for accounts that exist: made-up names must not be able to fill the audit trail
                    Audit(AuditActions.SignInFailed, user, "Wrong password", bySelf: false);
                    await _context.SaveChangesAsync();
                }

                return LoginFailed(request, "Invalid user name or password.");
            }

            if (user.VerifiedAt == null || !user.UStatus)
            {
                Audit(AuditActions.SignInFailed, user, user.VerifiedAt == null ? "Right password, but the account is not activated yet" : "Right password, but the account is disabled", bySelf: false);
                await _context.SaveChangesAsync();
            }

            if (user.VerifiedAt == null)
            {
                return LoginFailed(request, "Your account is not verified yet. Enter the token from the registration email under \"Activate your account\", or ask the XFL support team to activate it.");
            }

            if (!user.UStatus)
            {
                return LoginFailed(request, "You are currently inactive. Please contact the XFL team.");
            }

            // Start from an empty session: a previous sign-in in the same browser must not leave another role's key behind.
            HttpContext.Session.Clear();

            // The session copy is only used to identify the user; keep the credentials out of it.
            var jsonString = JsonConvert.SerializeObject(new User
            {
                Id = user.Id,
                FullName = user.FullName,
                Email = user.Email,
                PhonNumber = user.PhonNumber,
                Designation = user.Designation,
                BrokerageHouseName = user.BrokerageHouseName,
                BrokerageHouseAcronym = user.BrokerageHouseAcronym,
                Branch = user.Branch,
                Department = user.Department,
                EmployeeId = user.EmployeeId,
                UserName = user.UserName,
                VerifiedAt = user.VerifiedAt,
                UCatagory = user.UCatagory,
                UType = user.UType,
                UStatus = user.UStatus,
                Terms = user.Terms,
                PasswordHash = Array.Empty<byte>(),
                PasswordSalt = Array.Empty<byte>()
            });

            // The role follows from three columns of the user: see Rbac.RoleOf.
            var role = Rbac.RoleOf(user);
            HttpContext.Session.SetString(Rbac.SessionKey(role), jsonString);
            HttpContext.Session.SetString(SessionAuthorizeAttribute.Stamp, SessionAuthorizeAttribute.StampOf(user.PasswordHash));
            // the idle clock of this browser starts now (see NotificationHub.Clicked)
            HttpContext.RequestServices.GetRequiredService<XFLCSMS.Services.Notify.NotificationHub>().Clicked(HttpContext.Session.Id);

            Audit(AuditActions.SignIn, user, null);
            await _context.SaveChangesAsync();

            return RedirectToAction("Dashbord", Rbac.Controller(role));
        }

        private IActionResult LoginFailed(UserLoginRequest request, string message)
        {
            ViewBag.Message = message;
            request.Password = string.Empty;
            return View("Login", request);
        }

        [HttpPost]
        public async Task<IActionResult> ForgotPassword(Verify verify)
        {
            if (string.IsNullOrWhiteSpace(verify.Email))
            {
                ViewBag.Message = "Please enter your email or user name.";
                return View(verify);
            }

            var login = verify.Email.Trim();
            var user = await _context.Users.FirstOrDefaultAsync(u => (u.Email == login) || (u.UserName == login));
            if (user == null)
            {
                ViewBag.Message = "User not found.";
                return View(verify);
            }

            user.PasswordResetToken = CreateResetToken();
            user.ResetTokenExpires = DateTime.Now.AddDays(1);
            Audit(AuditActions.PasswordResetRequest, user, "Asked for a password reset token by e-mail", bySelf: false);
            await _context.SaveChangesAsync();

            try
            {
                _emailServices.SendEmail(new EmailDto
                {
                    // Always the address on file: the form also accepts a user name, which is not an email address.
                    To = user.Email,
                    Subject = "Password Reset Token",
                    Body = "Your Password Reset Token is: <b>" + user.PasswordResetToken + "</b><br/>It is valid for 24 hours."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not send the password reset email to {Email}", user.Email);
                ViewBag.Message = "We could not send the reset email. Please try again later.";
                return View(verify);
            }

            TempData["Message"] = "We emailed you a reset token. Enter it below together with your new password.";
            return RedirectToAction("ResetPassword");
        }

        [HttpPost]
        public async Task<IActionResult> ResetPassword(ResetPasswordRequest request)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.Message = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
                return View(request);
            }

            var token = request.Token.Trim();
            var user = await _context.Users.FirstOrDefaultAsync(u => u.PasswordResetToken == token);
            if (user == null || user.ResetTokenExpires < DateTime.Now)
            {
                ViewBag.Message = "Invalid or expired token.";
                return View(request);
            }

            PasswordHasher.Create(request.Password, out byte[] passwordHash, out byte[] passwordSalt);

            user.PasswordHash = passwordHash;
            user.PasswordSalt = passwordSalt;
            user.PasswordResetToken = null;
            user.ResetTokenExpires = null;
            Audit(AuditActions.PasswordReset, user, "Set a new password with the reset token");

            await _context.SaveChangesAsync();

            TempData["Message"] = "Your password was changed. You can sign in now.";
            return RedirectToAction("Login");
        }

        // Signed-in users change their password from their own menu (Admin/Maker/... ChangePassword).
        public IActionResult ChangePassword()
        {
            return RedirectToAction("Login");
        }

        [HttpGet]
        public async Task<IActionResult> GetBranches(int brokerageId)
        {
            // Only the two fields the registration page needs (no navigation properties).
            var branches = await _context.Branchhs
                .Where(b => b.BrokerageId == brokerageId)
                .Select(b => new { branchId = b.BranchId, branchName = b.BranchName })
                .ToListAsync();

            return Json(branches);
        }

        /// <summary>The token typed in to activate an account: six characters, entered together with the e-mail address.</summary>
        private string CreateRandomToken()
        {
            return Convert.ToHexString(RandomNumberGenerator.GetBytes(3));
        }

        /// <summary>
        /// The token for a new password. It is the only thing that page asks for, so it must not be guessable:
        /// 32 characters, copied from the e-mail.
        /// </summary>
        private string CreateResetToken()
        {
            return Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        }
    }
}
