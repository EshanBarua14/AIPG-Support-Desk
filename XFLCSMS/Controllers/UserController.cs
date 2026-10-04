using Microsoft.AspNetCore.Mvc;
using XFLCSMS.Services.EmailService;
using Microsoft.AspNetCore.Http;
using Org.BouncyCastle.Asn1.Ocsp;
using System.Security.Cryptography;
using XFLCSMS.Models.Register;
using XFLCSMS.Models.Email;
using XFLCSMS.Models.Login;
using Microsoft.EntityFrameworkCore.Metadata.Internal;

namespace XFLCSMS.Controllers
{
    public class UserController : Controller
    {
        [Route("api/[controller]")]
        [ApiController]
        public class UserControllers : ControllerBase
        {
            private readonly DataContext _context;

            private IEmailServices _EmailServices;

            public UserControllers(DataContext context, IEmailServices emailServices)
            {
                _context = context;
                _EmailServices = emailServices;
            }

            //public UserController(DataContext context, IEmailServices emailServices)
            //{
            //    _context = context;
            //    _EmailServices = emailServices;
            //}
            [HttpPost("register")]
            public async Task<IActionResult> Register(UserRegisterRequest request)
            {
                if (_context.Users.Any(u => (u.Email == request.Email || u.UserName == request.UserName)))
                {
                    return BadRequest("User already exists.");

                }

                CreatePasswordHash(request.Password,
                     out byte[] passwordHash,
                     out byte[] passwordSalt);

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
                    UserName = request.UserName
                };

                var sendMail = await _context.Users.AddAsync(user);
                await _context.SaveChangesAsync();

                if (sendMail != null)
                {
                    var Mail = new EmailDto
                    {
                        To = request.Email,
                        Subject = "Registration",
                        Body = "Dear valued customer, your registration is complete and your verification token is: " +
                        user.VerificationToken
                    };
                    _EmailServices.SendEmail(Mail);
                }


                return Ok("User successfully created!");
                //return View("User successfully created!");
            }

            [HttpPost("login")]
            public async Task<IActionResult> Login(UserLoginRequest request)
            {
                var user = await _context.Users.FirstOrDefaultAsync(u => (u.Email == request.UserId || u.UserName == request.UserId));
                if (user == null)
                {
                    return BadRequest("User not found.");
                }

                if (!VerifyPasswordHash(request.Password, user.PasswordHash, user.PasswordSalt))
                {
                    return BadRequest("Password is incorrect.");
                }

                if (user.VerifiedAt == null)
                {
                    return BadRequest("Not verified!");
                }

                return Ok($"Welcome back, {user.Email}! :)");
            }
            [HttpPost("verify")]
            public async Task<IActionResult> Verify(string token)
            {
                var user = await _context.Users.FirstOrDefaultAsync(u => u.VerificationToken == token);
                if (user == null)
                {
                    return BadRequest("Invalid token.");
                }

                user.VerifiedAt = DateTime.Now;
                await _context.SaveChangesAsync();

                return Ok("User verified! :)");
            }

            [HttpPost("forgot-password")]
            public async Task<IActionResult> ForgotPassword(string email)
            {
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
                if (user == null)
                {
                    return BadRequest("User not found.");
                }

                user.PasswordResetToken = CreateRandomToken();
                string ResetToken = user.PasswordResetToken;
                user.ResetTokenExpires = DateTime.Now.AddDays(1);
                await _context.SaveChangesAsync();

                if (ResetToken != null)
                {
                    var Mail = new EmailDto
                    {
                        To = email,
                        Subject = "Password Reset Token",
                        Body = "Your Password Reset Token is: " + ResetToken
                    };
                    _EmailServices.SendEmail(Mail);

                }

                return Ok("You may now reset your password.");
            }

            [HttpPost("reset-password")]
            public async Task<IActionResult> ResettPassword(ResetPasswordRequest request)
            {
                var user = await _context.Users.FirstOrDefaultAsync(u => u.PasswordResetToken == request.Token);
                if (user == null || user.ResetTokenExpires < DateTime.Now)
                {
                    return BadRequest("Invalid Token.");
                }

                CreatePasswordHash(request.Password, out byte[] passwordHash, out byte[] passwordSalt);

                user.PasswordHash = passwordHash;
                user.PasswordSalt = passwordSalt;
                user.PasswordResetToken = null;
                user.ResetTokenExpires = null;

                await _context.SaveChangesAsync();

                return Ok("Password successfully reset.");
            }




            private void CreatePasswordHash(string password, out byte[] passwordHash, out byte[] passwordSalt)
            {
                using (var hmac = new HMACSHA512())
                {
                    passwordSalt = hmac.Key;
                    passwordHash = hmac
                        .ComputeHash(System.Text.Encoding.UTF8.GetBytes(password));
                }
            }

            private bool VerifyPasswordHash(string password, byte[] passwordHash, byte[] passwordSalt)
            {
                using (var hmac = new HMACSHA512(passwordSalt))
                {
                    var computedHash = hmac
                        .ComputeHash(System.Text.Encoding.UTF8.GetBytes(password));
                    return computedHash.SequenceEqual(passwordHash);
                }
            }

            //private string CreateRandomToken()
            //{
            //    return Convert.ToHexString(RandomNumberGenerator.GetBytes(64));
            //}
            private string CreateRandomToken()
            {
                return Convert.ToHexString(RandomNumberGenerator.GetBytes(3));
            }
        }
    }
}
