using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace XFLCSMS.Models.Audit
{
    /// <summary>
    /// One line of the audit trail: who did what to which item, and when. Rows are only ever added.
    /// There is deliberately no foreign key to Users or Issues: the line must survive when the user or the
    /// ticket is deleted, so the names are copied into the row.
    /// </summary>
    public class AuditLog
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        public DateTime At { get; set; }

        /// <summary>The signed-in user who did it; null for somebody who is not signed in (failed sign-in, registration).</summary>
        public int? UserId { get; set; }

        [MaxLength(200)]
        public string UserName { get; set; } = string.Empty;

        /// <summary>Role of that user at the time, e.g. "Support manager".</summary>
        [MaxLength(40)]
        public string? Role { get; set; }

        /// <summary>
        /// The brokerage house the event belongs to (the house of the ticket, of the user that was changed, ...).
        /// Null for events of the platform itself (XFL staff accounts, master data, system).
        /// A house administrator sees the lines of the own house only.
        /// </summary>
        public int? BrokerageId { get; set; }

        /// <summary>What happened, e.g. "ticket.assign". The list is in Services/AuditService.cs.</summary>
        [MaxLength(60)]
        public string Action { get; set; } = string.Empty;

        /// <summary>"Ticket", "User", "Branch", ... together with EntityId: the item it happened to.</summary>
        [MaxLength(40)]
        public string? EntityType { get; set; }

        public int? EntityId { get; set; }

        /// <summary>Ticket number, user name, ... as it was at the time.</summary>
        [MaxLength(200)]
        public string? EntityLabel { get; set; }

        /// <summary>One sentence for people: "Assigned to Engineer One (was unassigned)".</summary>
        public string? Details { get; set; }

        [MaxLength(64)]
        public string? Ip { get; set; }
    }
}
