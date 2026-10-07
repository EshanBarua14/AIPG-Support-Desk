using XFLCSMS.Models.Register;

namespace XFLCSMS.Models.Issue
{
    /// <summary>
    /// One ticket as shown on the "view ticket" and "edit ticket" pages of every role
    /// (the name is historical). Filled in CsmsController.ToMakerView; the edit form posts it back.
    /// </summary>
    public class MakerView
    {
        public int IssueId { get; set; }
        public string? TicketNumber { get; set; }
        public string? BrokerageHouse { get; set; }

        public DateTime? CreatedOn { get; set; }
        public string? CreatedBy { get; set; }
        public DateTime? AssgnOn { get; set; }
        /// <summary>Full name of the support engineer the ticket is assigned to.</summary>
        public string? AssgnBy { get; set; }
        /// <summary>User id of that engineer. Posted by the "Assigned to" field: empty = nobody, -1 = leave as it is.</summary>
        public int? AssignedToId { get; set; }
        public DateTime? ApproveOn { get; set; }
        public string? ApproveBy { get; set; }
        public DateTime? UpdatedOn { get; set; }
        public string? UpdatedBy { get; set; }
        public DateTime? CloseOn { get; set; }
        public int? ClosedBy { get; set; }
        public string? ClosedbyName { get; set; }

        public string? SupportType { get; set; }
        public string? SupportCatagory { get; set; }
        public string? SupportSubCatagory { get; set; }
        public string? AffectedSection { get; set; }

        public string? Priority { get; set; }
        public string? IssueTitle { get; set; }
        public string? TicketStatus { get; set; }
        public string? TicketDetails { get; set; }
        public string? IStatus { get; set; }
        public string? Command { get; set; }
        public int? attachmentId { get; set; }

        public ICollection<Attachment>? Attachments { get; set; }

        // Filled for the page only (never posted): what the signed-in user may do with this ticket, and its history.
        /// <summary>May change title, details, priority and files.</summary>
        public bool CanEdit { get; set; }
        /// <summary>May set status and comments (XFL staff working on the ticket).</summary>
        public bool CanWork { get; set; }
        /// <summary>Edit form: the status the form showed when it was opened (hidden field).</summary>
        public string? OriginalStatus { get; set; }
        /// <summary>Edit form: the engineer the form showed when it was opened: "none" for nobody, else the user id (hidden field).</summary>
        public string? OriginalAssignee { get; set; }
        /// <summary>The statuses the signed-in user may give the ticket now (empty: none).</summary>
        public List<XFLCSMS.Services.TicketStatus.Info> StatusChoices { get; set; } = new();
        /// <summary>The ticket is assigned to the signed-in engineer.</summary>
        public bool IsMine { get; set; }
        public List<XFLCSMS.Models.Audit.AuditLog> History { get; set; } = new();
        public ICollection<User>? SupportEngineers { get; set; }
    }
}
