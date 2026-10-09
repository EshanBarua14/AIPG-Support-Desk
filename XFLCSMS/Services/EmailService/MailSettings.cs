using MailKit.Security;
using XFLCSMS.Services;

namespace XFLCSMS.Services.EmailService
{
    /// <summary>
    /// The mail server in use. The settings entered on the notification settings page win; while that page was
    /// never filled in, the three values of appsettings.json (EmailHost, EmailUsername, EmailPassword) are used,
    /// so an installation that already sent mail keeps doing so.
    /// </summary>
    public class MailSettings
    {
        public const string HostKey = "mail.host";
        public const string PortKey = "mail.port";
        public const string SecurityKey = "mail.security";
        public const string UserKey = "mail.user";
        public const string PasswordKey = "mail.password";
        public const string FromKey = "mail.from";
        public const string FromNameKey = "mail.fromname";

        public string Host { get; private set; } = string.Empty;
        public int Port { get; private set; } = 587;
        /// <summary>"auto", "starttls", "ssl" or "none".</summary>
        public string Security { get; private set; } = "auto";
        public string User { get; private set; } = string.Empty;
        public string Password { get; private set; } = string.Empty;
        public string From { get; private set; } = string.Empty;
        public string FromName { get; private set; } = "AIPG Support Desk";
        /// <summary>"page" (entered in the application), "file" (appsettings.json) or "none".</summary>
        public string Source { get; private set; } = "none";
        /// <summary>The password stored by the page cannot be decrypted any more.</summary>
        public bool PasswordUnreadable { get; private set; }

        public bool IsConfigured => Host.Length > 0 && From.Length > 0;

        public SecureSocketOptions SocketOptions
        {
            get
            {
                switch (Security)
                {
                    case "ssl": return SecureSocketOptions.SslOnConnect;
                    case "starttls": return SecureSocketOptions.StartTls;
                    case "none": return SecureSocketOptions.None;
                    default: return Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
                }
            }
        }

        public static MailSettings Read(SettingsStore store, IConfiguration config)
        {
            var host = store.Get(HostKey);
            if (host != null)
            {
                var user = store.Get(UserKey, string.Empty);
                return new MailSettings
                {
                    Host = host.Trim(),
                    Port = store.GetInt(PortKey, 587),
                    Security = store.Get(SecurityKey, "auto"),
                    User = user,
                    Password = store.GetSecret(PasswordKey) ?? string.Empty,
                    PasswordUnreadable = store.SecretIsUnreadable(PasswordKey),
                    From = store.Get(FromKey, user),
                    FromName = store.Get(FromNameKey, "AIPG Support Desk"),
                    Source = "page"
                };
            }

            var fileHost = config["EmailHost"];
            var fileUser = config["EmailUsername"];
            if (string.IsNullOrWhiteSpace(fileHost) || string.IsNullOrWhiteSpace(fileUser))
            {
                return new MailSettings();
            }

            return new MailSettings
            {
                Host = fileHost.Trim(),
                User = fileUser,
                Password = config["EmailPassword"] ?? string.Empty,
                From = fileUser,
                Source = "file"
            };
        }
    }
}
