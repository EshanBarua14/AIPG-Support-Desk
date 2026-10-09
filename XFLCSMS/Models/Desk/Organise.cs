using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using XFLCSMS.Models.Issue;

namespace XFLCSMS.Models.Desk
{
    /// <summary>
    /// A label AIPG staff put on tickets to find them again ("regression", "waiting for exchange", "release 4.2").
    /// Tags are for the support team: the people of the brokerage houses do not see them.
    /// </summary>
    public class Tag
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [MaxLength(40)]
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>A tag on a ticket.</summary>
    public class TicketTag
    {
        public int IssueId { get; set; }
        public IssueTable? Issue { get; set; }

        public int TagId { get; set; }
        public Tag? Tag { get; set; }
    }

    /// <summary>
    /// Two tickets that belong together: the same problem raised twice ("duplicate"), or tickets that have to do
    /// with each other ("related"). Stored once, shown on both tickets.
    /// </summary>
    public class TicketLink
    {
        public const string Related = "related";
        public const string Duplicate = "duplicate";

        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        /// <summary>The ticket the link was made on. For a duplicate: the ticket that IS the duplicate.</summary>
        public int IssueId { get; set; }

        /// <summary>The other ticket. For a duplicate: the ticket that is worked on. (A number only: see DataContext.)</summary>
        public int OtherIssueId { get; set; }

        [MaxLength(20)]
        public string Kind { get; set; } = Related;

        public DateTime CreatedAt { get; set; }

        [MaxLength(200)]
        public string? CreatedBy { get; set; }
    }

    /// <summary>
    /// Somebody who wants to hear what happens on a ticket without having raised it or working on it (a manager,
    /// a second engineer, the administrator of the brokerage house).
    /// </summary>
    public class TicketWatcher
    {
        public int IssueId { get; set; }
        public int UserId { get; set; }
        public DateTime Since { get; set; }
    }
}
