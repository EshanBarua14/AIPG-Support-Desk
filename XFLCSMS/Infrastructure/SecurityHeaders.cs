using System.Security.Cryptography;

namespace XFLCSMS.Infrastructure
{
    /// <summary>
    /// Response headers that tell the browser what a page of this site may do. They cost nothing and close whole
    /// classes of attack that would otherwise each need their own fix:
    ///   Content-Security-Policy   scripts run only from this site, or inline with this response's nonce; no plug-ins,
    ///                             no forms posting elsewhere, no page of ours inside somebody else's frame
    ///   X-Frame-Options           the same "no framing" for older browsers (clickjacking)
    ///   X-Content-Type-Options    an uploaded .txt is never run as a script because it looks like one
    ///   Referrer-Policy           addresses of tickets do not leak to other sites through links
    ///   Permissions-Policy        the site never needs camera, microphone or location
    ///
    /// Inline &lt;script&gt; blocks in the views get the nonce from <see cref="ScriptNonceTagHelper"/>; nothing in a
    /// view has to change. Styles keep 'unsafe-inline' because the views use style="" attributes.
    ///
    /// "Security:ContentSecurityPolicy" in appsettings.json: "enforce" (default), "report" (the browser only writes
    /// violations to its console - for trying out a change), "off".
    /// </summary>
    public static class SecurityHeaders
    {
        private const string NonceKey = "csp-nonce";

        /// <summary>The nonce of this response: random, new for every request.</summary>
        public static string Nonce(HttpContext context)
        {
            if (context.Items[NonceKey] is string nonce)
            {
                return nonce;
            }

            nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(18));
            context.Items[NonceKey] = nonce;
            return nonce;
        }

        public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app, IConfiguration configuration)
        {
            var mode = (configuration["Security:ContentSecurityPolicy"] ?? "enforce").Trim().ToLowerInvariant();

            return app.Use(async (context, next) =>
            {
                var nonce = Nonce(context);
                context.Response.OnStarting(() =>
                {
                    var headers = context.Response.Headers;
                    headers["X-Content-Type-Options"] = "nosniff";
                    headers["X-Frame-Options"] = "DENY";
                    headers["Referrer-Policy"] = "same-origin";
                    headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
                    headers["Cross-Origin-Opener-Policy"] = "same-origin";

                    if (mode != "off")
                    {
                        var policy =
                            "default-src 'self'; " +
                            "script-src 'self' 'nonce-" + nonce + "'; " +
                            "style-src 'self' 'unsafe-inline'; " +
                            "img-src 'self' data: blob:; " +
                            "font-src 'self'; " +
                            "connect-src 'self'; " +
                            "object-src 'none'; " +
                            "base-uri 'self'; " +
                            "form-action 'self'; " +
                            "frame-ancestors 'none'";
                        headers[mode == "report" ? "Content-Security-Policy-Report-Only" : "Content-Security-Policy"] = policy;
                    }

                    return Task.CompletedTask;
                });

                await next();
            });
        }
    }
}
