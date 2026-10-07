using XFLCSMS.Models.Notify;
using XFLCSMS.Services.EmailService;

namespace XFLCSMS.Models.Admin
{
    /// <summary>The notification settings page: channels, mail server, SMS gateway, events, what was sent.</summary>
    public class NotificationSettingsView
    {
        public bool InApp { get; set; }
        public bool Email { get; set; }
        public bool Sms { get; set; }
        public string? SiteUrl { get; set; }
        /// <summary>Address of this request, shown as the value used while "site address" is empty.</summary>
        public string RequestUrl { get; set; } = string.Empty;

        public MailSettings Mail { get; set; } = new();
        public bool MailHasPassword { get; set; }

        public string SmsUrl { get; set; } = string.Empty;
        public string SmsMethod { get; set; } = "POST";
        public string SmsContentType { get; set; } = "form";
        public string SmsBody { get; set; } = string.Empty;
        public string SmsHeader { get; set; } = string.Empty;
        public string SmsSender { get; set; } = string.Empty;
        public string SmsPrefix { get; set; } = string.Empty;
        public string SmsSuccess { get; set; } = string.Empty;
        public bool SmsHasKey { get; set; }
        public bool SmsKeyUnreadable { get; set; }
        public bool SmsConfigured { get; set; }

        /// <summary>"ticket.created:email" for every switch that is on.</summary>
        public HashSet<string> On { get; set; } = new();

        public int Queued { get; set; }
        public int Failed { get; set; }
        public List<NotificationDelivery> Recent { get; set; } = new();
        public DateTime? WorkerLastRun { get; set; }

        public string MyEmail { get; set; } = string.Empty;
        public string MyPhone { get; set; } = string.Empty;
    }

    /// <summary>The bell page of one user: his notifications and what he wants to get.</summary>
    public class NotificationsView
    {
        public List<Notification> Items { get; set; } = new();
        public int Unread { get; set; }
        public int Total { get; set; }
        public int Page { get; set; } = 1;
        public int Pages { get; set; } = 1;
        public bool OnlyUnread { get; set; }

        public bool WantInApp { get; set; } = true;
        public bool WantEmail { get; set; } = true;
        public bool WantSms { get; set; } = true;
        // what the system has switched on at all
        public bool InAppOn { get; set; }
        public bool EmailOn { get; set; }
        public bool SmsOn { get; set; }
        public string MyEmail { get; set; } = string.Empty;
        public string MyPhone { get; set; } = string.Empty;
    }
}
