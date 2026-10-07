using XFLCSMS.Models.Email;

namespace XFLCSMS.Services.EmailService
{
    public interface IEmailServices
    {
        void SendEmail(EmailDto request);

        /// <summary>The mail server in use (notification settings page, else appsettings.json).</summary>
        MailSettings Settings { get; }

        /// <summary>
        /// Connects and signs in to the mail server without sending anything.
        /// Returns null when that works, otherwise the reason it did not.
        /// </summary>
        string? TestConnection();
    }
}
