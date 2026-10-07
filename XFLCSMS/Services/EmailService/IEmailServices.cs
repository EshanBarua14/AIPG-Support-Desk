using XFLCSMS.Models.Email;

namespace XFLCSMS.Services.EmailService
{
    public interface IEmailServices
    {
        void SendEmail(EmailDto request);

        /// <summary>
        /// Connects and signs in to the mail server without sending anything.
        /// Returns null when that works, otherwise the reason it did not.
        /// </summary>
        string? TestConnection();
    }
}
