using MailKit.Security;
using MimeKit.Text;
using MimeKit;
using MailKit.Net.Smtp;
using XFLCSMS.Models.Email;

namespace XFLCSMS.EmailService
{
    public class EmailService:IEmailServices
    {
        private readonly IConfiguration _config;

        public EmailService(IConfiguration config)
        {
            _config = config;
        }

        public void SendEmail(EmailDto request)
        {
            var email = new MimeMessage();
            email.From.Add(MailboxAddress.Parse(_config.GetSection("EmailUsername").Value));
            email.To.Add(MailboxAddress.Parse(request.To));
            email.Subject = request.Subject;
            email.Body = new TextPart(TextFormat.Html) { Text = request.Body };

            using var smtp = new SmtpClient();
            smtp.Timeout = 20000; // do not let a dead mail server hang the web request for minutes
            smtp.Connect(_config.GetSection("EmailHost").Value, 587, SecureSocketOptions.StartTls);
            smtp.Authenticate(_config.GetSection("EmailUsername").Value, _config.GetSection("EmailPassword").Value);
            smtp.Send(email);
            smtp.Disconnect(true);
        }

        public string? TestConnection()
        {
            try
            {
                using var smtp = new SmtpClient();
                smtp.Timeout = 10000;
                smtp.Connect(_config.GetSection("EmailHost").Value, 587, SecureSocketOptions.StartTls);
                smtp.Authenticate(_config.GetSection("EmailUsername").Value, _config.GetSection("EmailPassword").Value);
                smtp.Disconnect(true);
                return null;
            }
            catch (Exception exception)
            {
                // the text goes to an administrator; it never contains the password
                var message = (exception.InnerException ?? exception).Message.Replace("\r", " ").Replace("\n", " ");
                return exception.GetType().Name + ": " + (message.Length > 300 ? message.Substring(0, 300) : message);
            }
        }
    }
}
