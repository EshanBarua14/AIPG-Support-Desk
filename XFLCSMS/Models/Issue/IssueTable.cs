using XFLCSMS.Models.Affected;
using XFLCSMS.Models.Branch;
using XFLCSMS.Models.Brocarage;
using XFLCSMS.Models.Support;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using XFLCSMS.Models.Register;

namespace XFLCSMS.Models.Issue
{
    public class IssueTable
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int IssueId { get; set; }

        [Required]
        [ForeignKey("Users")]
        public int UserId { get; set; }
        public User Users { get; set; }
        [Required]
        [ForeignKey("Brokerages")]
        public int BrokerageId { get; set; }
        public Brokerage Brokerages { get; set; }

        public DateTime TDate { get; set; }
        public string TNumber { get; set; } = string.Empty;

        public DateTime? AssignOn { get; set; }
        public string? AssignBy { get; set; } = string.Empty;

        /// <summary>
        /// User id of the support engineer the ticket is assigned to. AssignBy keeps the engineer's name for display.
        /// Tickets assigned before this column existed have the name only (see TicketService.AssignedTo).
        /// No foreign key on purpose: deleting a user must not be blocked by, or cascade into, old tickets.
        /// </summary>
        public int? AssignedToId { get; set; }
        public DateTime? ApproveOn { get; set; }
        public string? ApproveBy { get; set; } = string.Empty;

        public DateTime? UpdatedOn { get; set; }

        public string? UpdatedBy { get; set; }= string.Empty;

        /// <summary>The AIPG product the ticket is about; empty when the person who raised it was not sure.</summary>
        public int? ProductId { get; set; }
        public Product? Product { get; set; }

        [ForeignKey("supportTypes")]
        public int? SupportTypeId { get; set; }
        public SupportType? supportTypes { get; set; }
       
        [ForeignKey("supportCatagorys")]
        public int? SupportCatagoryId { get; set; }
        public SupportCatagory? supportCatagorys { get; set; }
        
        [ForeignKey("SupportSubCatagorys")]
       public int? SupportSubCatagoryId { get; set; }
        public SupportSubCatagory? SupportSubCatagorys { get; set; }
        //[ForeignKey("AffectedSectionId")]
        
        [ForeignKey("affecteds")]
        public int? AffectedSectionId { get; set; }
        public AffectedSection? affecteds { get; set; }
        

        public string Priority { get; set; }=string.Empty;
        public string? ITitle { get; set; } = string.Empty;
        public string? Details { get; set; } 

        public string? Comments { get; set; } 

        public ICollection<Attachment>? attachment { get; set; }

        public string? IStatus { get; set; } = string.Empty;

        public DateTime? ClosedOn { get; set; }
        public string? ClosedBy { get; set; }

        // ---- service times (Services/Sla*.cs). All null for tickets raised before version 3.3: they have no targets.

        /// <summary>When AIPG first answered: the first reply of staff the house can read, or the start of the work.</summary>
        public DateTime? FirstResponseAt { get; set; }

        /// <summary>When the ticket was solved (first Done, Deployed or Closed). Cleared when it is reopened.</summary>
        public DateTime? ResolvedAt { get; set; }

        /// <summary>Target for the first response, from the service targets in force when the ticket was raised.</summary>
        public DateTime? ResponseDueAt { get; set; }

        /// <summary>Target for the solution. Moves later by the time the ticket spends in "Pending".</summary>
        public DateTime? ResolveDueAt { get; set; }

        /// <summary>Since when the ticket is "Pending" (the clock for the solution stands still), else null.</summary>
        public DateTime? PendingSince { get; set; }

        /// <summary>Working minutes the ticket has spent in "Pending" so far (they do not count towards the solution).</summary>
        public int SlaPausedMinutes { get; set; }

        /// <summary>Which warnings about the targets were already sent (bits of SlaNotice), so each goes out once.</summary>
        public int SlaNotices { get; set; }

        /// <summary>How often the ticket was reopened after it had been solved or closed.</summary>
        public int ReopenCount { get; set; }

        // ---- automation (Services/Automation.cs)

        /// <summary>Since when the ticket has the status it has now. Null for tickets whose status did not change since version 3.4.</summary>
        public DateTime? StatusSince { get; set; }

        /// <summary>How often the person who raised the ticket was reminded while it is "Pending". Back to 0 when it leaves that status.</summary>
        public int ReminderCount { get; set; }

        public DateTime? LastReminderAt { get; set; }

        // ---- what the person who raised it thought of the support

        /// <summary>1 (bad) to 5 (very good), given once by the person who raised the ticket after it was closed.</summary>
        public int? Rating { get; set; }

        [MaxLength(1000)]
        public string? RatingComment { get; set; }

        public DateTime? RatedAt { get; set; }

      

    }
}
