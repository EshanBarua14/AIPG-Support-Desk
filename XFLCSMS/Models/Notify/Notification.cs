using System.ComponentModel.DataAnnotations;

namespace XFLCSMS.Models.Notify
{
    /// <summary>
    /// Something a user is told: shown as a toast while he is signed in, and kept in his list (the bell).
    /// E-mail and SMS copies are separate rows (<see cref="NotificationDelivery"/>).
    /// </summary>
    public class Notification
    {
        public long Id { get; set; }

        /// <summary>The user who is told.</summary>
        public int UserId { get; set; }

        public DateTime At { get; set; }

        /// <summary>The event, e.g. "ticket.assigned" (Services/NotificationEvents).</summary>
        [StringLength(40)]
        public string Kind { get; set; } = string.Empty;

        [StringLength(200)]
        public string Title { get; set; } = string.Empty;

        [StringLength(1000)]
        public string? Body { get; set; }

        /// <summary>Page to open, in the area of the reader: an action name ("TicketView") and the id for it.</summary>
        [StringLength(60)]
        public string? LinkAction { get; set; }

        public int? LinkId { get; set; }

        /// <summary>Null while unread.</summary>
        public DateTime? ReadAt { get; set; }
    }

    /// <summary>
    /// One e-mail or SMS to send. The row is written in the same transaction as the change it reports and sent
    /// afterwards by a background worker (Services/NotificationWorker.cs), so a slow or broken mail server never
    /// holds up a page and nothing is lost when the application restarts.
    /// </summary>
    public class NotificationDelivery
    {
        public const string Email = "email";
        public const string Sms = "sms";

        public const string Queued = "queued";
        public const string Sent = "sent";
        public const string Failed = "failed";
        public const string Skipped = "skipped";

        public long Id { get; set; }

        public int? UserId { get; set; }

        [StringLength(40)]
        public string Kind { get; set; } = string.Empty;

        /// <summary>"email" or "sms".</summary>
        [StringLength(10)]
        public string Channel { get; set; } = Email;

        /// <summary>E-mail address or phone number.</summary>
        [StringLength(200)]
        public string Recipient { get; set; } = string.Empty;

        [StringLength(200)]
        public string? Subject { get; set; }

        public string Body { get; set; } = string.Empty;

        /// <summary>"queued", "sent", "failed" or "skipped".</summary>
        [StringLength(10)]
        public string Status { get; set; } = Queued;

        public int Attempts { get; set; }

        public DateTime CreatedAt { get; set; }

        /// <summary>Not before this time (set after a failed attempt).</summary>
        public DateTime? NextTryAt { get; set; }

        public DateTime? DoneAt { get; set; }

        [StringLength(500)]
        public string? Error { get; set; }
    }

    /// <summary>What one user wants. No row means: everything that is switched on for the system.</summary>
    public class NotificationPreference
    {
        [Key]
        [System.ComponentModel.DataAnnotations.Schema.DatabaseGenerated(System.ComponentModel.DataAnnotations.Schema.DatabaseGeneratedOption.None)]
        public int UserId { get; set; }

        public bool InApp { get; set; } = true;
        public bool Email { get; set; } = true;
        public bool Sms { get; set; } = true;
    }
}
