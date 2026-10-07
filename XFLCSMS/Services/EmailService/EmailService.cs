using MailKit.Security;
using MimeKit.Text;
using MimeKit;
using MailKit.Net.Smtp;
using XFLCSMS.Models.Email;
using XFLCSMS.Services;

namespace XFLCSMS.EmailService
{
    public class EmailService:IEmailServices
    {
        private readonly IConfiguration _config;
        private readonly SettingsStore _settings;

        public EmailService(IConfiguration config, SettingsStore settings)
        {
            _config = config;
            _settings = settings;
        }

        /// <summary>The mail server in use: the notification settings page, else appsettings.json.</summary>
        public MailSettings Settings => MailSettings.Read(_settings, _config);

        public void SendEmail(EmailDto request)
        {
            var settings = Settings;
            if (!settings.IsConfigured)
            {
                throw new InvalidOperationException("No mail server is set up (Notification settings > E-mail).");
            }

            var email = new MimeMessage();
            email.From.Add(new MailboxAddress(settings.FromName, settings.From));
            email.To.Add(MailboxAddress.Parse(request.To));
            email.Subject = request.Subject;
            email.Body = new TextPart(TextFormat.Html) { Text = request.Body };

            using var smtp = new SmtpClient();
            smtp.Timeout = 20000; // do not let a dead mail server hang the web request for minutes
            smtp.Connect(settings.Host, settings.Port, settings.SocketOptions);
            if (settings.User.Length > 0)
            {
                smtp.Authenticate(settings.User, settings.Password);
            }

            smtp.Send(email);
            smtp.Disconnect(true);
        }

        public string? TestConnection()
        {
            try
            {
                var settings = Settings;
                if (!settings.IsConfigured)
                {
                    return "No mail server is set up.";
                }

                using var smtp = new SmtpClient();
                smtp.Timeout = 10000;
                smtp.Connect(settings.Host, settings.Port, settings.SocketOptions);
                if (settings.User.Length > 0)
                {
                    smtp.Authenticate(settings.User, settings.Password);
                }

                smtp.Disconnect(true);
                return null;
            }
            catch (Exception exception)
            {
                // the text goes to an administrator; it never contains the password
                return Describe(exception);
            }
        }

        public static string Describe(Exception exception)
        {
            var message = (exception.InnerException ?? exception).Message.Replace("\r", " ").Replace("\n", " ");
            return exception.GetType().Name + ": " + (message.Length > 300 ? message.Substring(0, 300) : message);
        }
    }
}
