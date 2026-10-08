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
        private readonly AttemptGuard _guard;
        private readonly SignInLimits _limits;
        private readonly IConfiguration _configuration;

        public RegisterLoginController(DataContext context, IEmailServices emailServices, ILogger<RegisterLoginController> logger, AuditService audit,
            AttemptGuard guard, SignInLimits limits, IConfiguration configuration)
        {
            _context = context;
            _emailServices = emailServices;
            _logger = logger;
            _audit = audit;
            _guard = guard;
            _limits = limits;
            _configuration = configuration;
        }

        // ---- limits on guessing -------------------------------------------------------------------------------
        // Two counters stand between a stranger and a password or token:
        //   - per account: after MaxFailuresPerAccount wrong tries in a row the account is locked for LockMinutes
        //     (User.FailedAttempts / LockedUntil; an administrator can unlock it under Users);
        //   - per network address: after MaxFailuresPerAddress failures inside the window, every try from that
        //     address is refused for a while, whatever name it uses.
        // Names without an account are counted too (in memory) and answered exactly like real ones, so the page
        // never tells a stranger which names are registered.

        private const string WrongSignIn = "Invalid user name or password.";

        private string LockedMessage =>
            "Too many wrong attempts. For your protection this sign-in is locked for " + Minutes(_limits.LockMinutes)
            + ". Try again later, use \u201cForgot your password?\u201d, or ask an administrator to unlock the account.";

        private string AddressMessage =>
            "Too many failed attempts from this computer or network. Wait " + Minutes(_limits.AddressWindowMinutes) + " and try again.";

        private static string Minutes(int minutes) => minutes == 1 ? "1 minute" : minutes + " minutes";

        /// <summary>Where the request comes from, as the key of the per-address counter.</summary>
        private string AddressKey(string what)
        {
            var address = HttpContext.Connection.RemoteIpAddress;
            if (address != null && address.IsIPv4MappedToIPv6)
            {
                address = address.MapToIPv4();
            }

            return what + ":" + (address?.ToString() ?? "unknown");
        }

        private bool AddressBlocked(string what) => _guard.IsBlocked(AddressKey(what), _limits.MaxFailuresPerAddress, _limits.AddressWindow);

        private void AddressFailed(string what) => _guard.Fail(AddressKey(what), _limits.AddressWindow);

        /// <summary>The page again with "too many attempts", as HTTP 429 so that scripts and proxies understand it too.</summary>
        private IActionResult TooMany(string view, object? model)
        {
            ViewBag.Message = AddressMessage;
            Response.StatusCode = StatusCodes.Status429TooManyRequests;
            return View(view, model);
        }

        /// <summary>Is this name (an account, or a name without one) locked right now?</summary>
        private bool IsLocked(User? user, string login)
        {
            return user != null
                ? user.LockedUntil != null && user.LockedUntil > DateTime.Now
                : _guard.IsBlocked("name:" + login, _limits.MaxFailuresPerAccount, _limits.LockTime);
        }

        /// <summary>
        /// One wrong password or token. Counts it on the account (or on the name, when there is no account) and on the
        /// network address. Returns true when this try locked the name. The caller saves the changes.
        /// </summary>
        private bool CountFailure(User? user, string login, string what, string detail)
        {
            AddressFailed(what);

            if (user == null)
            {
                return _guard.Fail("name:" + login, _limits.LockTime) >= _limits.MaxFailuresPerAccount;
            }

            user.FailedAttempts++;
            if (user.FailedAttempts < _limits.MaxFailuresPerAccount)
            {
                // only for accounts that exist: made-up names must not be able to fill the audit trail
                Audit(AuditActions.SignInFailed, user, detail + " (" + user.FailedAttempts + " of " + _limits.MaxFailuresPerAccount + " before the account is locked)", bySelf: false);
                return false;
            }

            user.FailedAttempts = 0;
            user.LockedUntil = DateTime.Now.Add(_limits.LockTime);
            Audit(AuditActions.SignInLocked, user, detail + ", " + _limits.MaxFailuresPerAccount + " times in a row: locked until " + user.LockedUntil.Value.ToString("HH:mm"), bySelf: false);
            _logger.LogWarning("Account {UserName} is locked until {Until} after {Count} wrong attempts from {Address}", user.UserName, user.LockedUntil, _limits.MaxFailuresPerAccount, AddressKey(what));
            return true;
        }

        private static bool SameToken(string? stored, string typed)
        {
            // tokens are letters and digits; people type them in either case
            return !string.IsNullOrEmpty(stored)
                && CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.UTF8.GetBytes(stored.ToUpperInvariant()),
                    System.Text.Encoding.UTF8.GetBytes(typed.ToUpperInvariant()));
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

            // Registration is open to everybody. A script must not be able to fill the list of waiting accounts:
            // ten registrations an hour from one address are plenty for a real office.
            if (_guard.IsBlocked(AddressKey("register"), 10, TimeSpan.FromHours(1)))
            {
                ModelState.AddModelError(string.Empty, "Too many registrations from this computer or network. Try again in an hour, or ask your administrator to create the account.");
                Response.StatusCode = StatusCodes.Status429TooManyRequests;
                return RegisterForm(registerView);
            }

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

            _guard.Fail(AddressKey("register"), TimeSpan.FromHours(1));
            Audit(AuditActions.Register, user, "Registered with " + user.Email + "; waits for activation");
            // the administrators who can activate the account are told (bell, and e-mail if that is switched on)
            HttpContext.RequestServices.GetRequiredService<XFLCSMS.Services.Notify.NotificationService>().AccountWaiting(user);
            await _context.SaveChangesAsync();

            try
            {
                _emailServices.SendEmail(new EmailDto
                {
                    To = request.Email,
                    Subject = "Xpert CSMS: your registration token",
                    Body = "Dear Concern,<br/>" +
                           "Thank you for registering in Xpert CSMS.<br/><br/>" +
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

            if (AddressBlocked("verify"))
            {
                return TooMany("Verify", verify);
            }

            var token = verify.Token.Trim();
            var login = verify.Email.Trim();
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == login || u.UserName == login);

            if (IsLocked(user, login))
            {
                ViewBag.Message = LockedMessage;
                return View(verify);
            }

            if (user == null || !SameToken(user.VerificationToken, token))
            {
                var locked = CountFailure(user, login, "verify", "Wrong activation token");
                await _context.SaveChangesAsync();
                ViewBag.Message = locked ? LockedMessage : "Invalid token or email.";
                return View(verify);
            }

            user.FailedAttempts = 0;
            if (user.VerifiedAt == null)
            {
                user.VerifiedAt = DateTime.Now;
                Audit(AuditActions.Verify, user, "Activated the account with the token from the e-mail");
            }

            await _context.SaveChangesAsync();

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

            if (AddressBlocked("signin"))
            {
                request.Password = string.Empty;
                return TooMany("Login", request);
            }

            var login = request.UserId.Trim();
            var user = await _context.Users.FirstOrDefaultAsync(u => (u.Email == login || u.UserName == login));

            // A locked name is refused before the password is looked at: the right password does not open it either,
            // otherwise the lock would only slow down the person who does not know it.
            if (IsLocked(user, login))
            {
                return LoginFailed(request, LockedMessage);
            }

            // Same message, and the same amount of work, for "no such user" and "wrong password", so the form does
            // not reveal who is registered.
            var passwordIsRight = user != null && PasswordHasher.Verify(request.Password, user.PasswordHash, user.PasswordSalt);
            if (user == null)
            {
                PasswordHasher.SpendTime(request.Password);
            }

            if (!passwordIsRight)
            {
                var locked = CountFailure(user, login, "signin", "Wrong password");
                await _context.SaveChangesAsync();
                return LoginFailed(request, locked ? LockedMessage : WrongSignIn);
            }

            user!.FailedAttempts = 0;
            user.LockedUntil = null;

            // The only moment the clear password is at hand: bring a password saved in the old, fast form (before
            // version 3.2), or with fewer rounds than today, up to date. The person notices nothing.
            if (PasswordHasher.NeedsUpgrade(user.PasswordHash))
            {
                PasswordHasher.Create(request.Password, out byte[] upgradedHash, out byte[] upgradedSalt);
                user.PasswordHash = upgradedHash;
                user.PasswordSalt = upgradedSalt;
            }

            // The password of the first administrator is written in a settings file (and that file has been in the
            // repository): whoever still signs in with it has to choose a new one before doing anything else.
            var seedName = _configuration["SeedAdmin:UserName"];
            var seedPassword = _configuration["SeedAdmin:Password"];
            if (!string.IsNullOrEmpty(seedPassword) && request.Password == seedPassword
                && string.Equals(user.UserName, seedName, StringComparison.OrdinalIgnoreCase))
            {
                user.MustChangePassword = true;
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

            // every request counts here, not only the failed ones: each one can send an e-mail
            if (AddressBlocked("forgot"))
            {
                return TooMany("ForgotPassword", verify);
            }

            AddressFailed("forgot");

            var login = verify.Email.Trim();
            var user = await _context.Users.FirstOrDefaultAsync(u => (u.Email == login) || (u.UserName == login));

            // The answer is the same whether or not the name has an account ("User not found" told a stranger who is
            // registered). A second request within two minutes sends nothing: the first mail is still on its way.
            var justSent = user?.ResetTokenExpires != null && user.ResetTokenExpires > DateTime.Now.AddDays(1).AddMinutes(-2);
            if (user != null && !justSent)
            {
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
                        Subject = "Xpert CSMS: your password reset token",
                        Body = "Your Password Reset Token is: <b>" + user.PasswordResetToken + "</b><br/>It is valid for 24 hours."
                            + "<br/><br/>If you did not ask for it, you can ignore this e-mail: your password stays as it is."
                    });
                }
                catch (Exception ex)
                {
                    // Not shown on the page (that would again tell who is registered). The person asks an administrator,
                    // who sets a password under Users; the failure is in the log and on the system health page.
                    _logger.LogError(ex, "Could not send the password reset email to {Email}", user.Email);
                }
            }

            TempData["Message"] = "If an account with that e-mail address or user name exists, we have e-mailed a reset token to its address. "
                + "Enter it below together with your new password. No e-mail after a few minutes? Ask your administrator to set a password for you.";
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

            if (AddressBlocked("reset"))
            {
                return TooMany("ResetPassword", request);
            }

            var token = request.Token.Trim();
            var user = await _context.Users.FirstOrDefaultAsync(u => u.PasswordResetToken == token);
            if (user == null || user.ResetTokenExpires < DateTime.Now)
            {
                AddressFailed("reset");
                ViewBag.Message = "Invalid or expired token.";
                return View(request);
            }

            // the rules that need the owner: not the user name, not the e-mail address
            var problem = PasswordPolicy.Problem(request.Password, user.UserName, user.Email);
            if (problem != null)
            {
                ViewBag.Message = problem;
                return View(request);
            }

            PasswordHasher.Create(request.Password, out byte[] passwordHash, out byte[] passwordSalt);

            user.PasswordHash = passwordHash;
            user.PasswordSalt = passwordSalt;
            user.PasswordResetToken = null;
            user.ResetTokenExpires = null;
            // the token came to the owner's mailbox: that also ends a lock, and the password is their own choice now
            user.FailedAttempts = 0;
            user.LockedUntil = null;
            user.MustChangePassword = false;
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

        /// <summary>
        /// The token typed in to activate an account, entered together with the e-mail address: eight characters
        /// from 31 letters and digits that cannot be mistaken for each other (no 0/O, 1/I/L). That is some 850
        /// thousand million possibilities, and a few wrong tries lock the account.
        /// </summary>
        private string CreateRandomToken()
        {
            const string alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
            var token = new char[8];
            for (var i = 0; i < token.Length; i++)
            {
                token[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
            }

            return new string(token);
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
