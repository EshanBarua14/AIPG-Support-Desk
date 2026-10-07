using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace XFLCSMS.Infrastructure
{
    /// <summary>
    /// The application signs users in by storing their record in the session under a role specific key
    /// (AdminData, SMData, SEData, HouseAdminData, MakerData). This filter refuses the request when none of the accepted
    /// keys is present, so an action can no longer be reached just by typing its URL.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class SessionAuthorizeAttribute : ActionFilterAttribute
    {
        public const string Admin = "AdminData";
        public const string SupportManager = "SMData";
        public const string SupportEngineer = "SEData";
        public const string Maker = "MakerData";
        public const string HouseAdmin = "HouseAdminData";

        public static readonly string[] AllKeys = { Admin, SupportManager, SupportEngineer, HouseAdmin, Maker };

        /// <summary>Session entry that changes whenever the password changes (see <see cref="StampOf"/>).</summary>
        public const string Stamp = "Stamp";

        /// <summary>The role a user signs in with, as the session key of that role. The one place that decides it.</summary>
        public static string KeyFor(bool isAdmin, bool isXflStaff, string? department)
        {
            return Rbac.SessionKey(Rbac.RoleOf(isAdmin, isXflStaff, department));
        }

        /// <summary>
        /// A short fingerprint of the stored password hash. It is kept in the (server side) session at sign-in and
        /// compared on every request, so a new password signs out every other browser that still uses the old one.
        /// </summary>
        public static string StampOf(byte[]? passwordHash)
        {
            var digest = System.Security.Cryptography.SHA256.HashData(passwordHash ?? Array.Empty<byte>());
            return Convert.ToHexString(digest, 0, 8);
        }

        private readonly string[] _sessionKeys;

        /// <param name="sessionKeys">Accepted session keys; none means "any signed-in user".</param>
        public SessionAuthorizeAttribute(params string[] sessionKeys)
        {
            _sessionKeys = sessionKeys == null || sessionKeys.Length == 0 ? AllKeys : sessionKeys;
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            if (context.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any())
            {
                return; // e.g. the error page
            }

            var session = context.HttpContext.Session;
            if (_sessionKeys.Any(key => !string.IsNullOrEmpty(session.GetString(key))))
            {
                return;
            }

            context.Result = Challenge(context.HttpContext.Request);
        }

        /// <summary>jQuery AJAX calls, and the fetch() DELETE calls used by the list pages.</summary>
        public static bool IsAjax(HttpRequest request)
        {
            return string.Equals(request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase)
                || HttpMethods.IsDelete(request.Method);
        }

        /// <summary>Login redirect for normal page requests, 401 for AJAX calls.</summary>
        public static IActionResult Challenge(HttpRequest request)
        {
            if (IsAjax(request))
            {
                return new UnauthorizedObjectResult("Your session has expired. Please sign in again.");
            }

            return new RedirectToActionResult("Login", "RegisterLogin", null);
        }
    }
}
