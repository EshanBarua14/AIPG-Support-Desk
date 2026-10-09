using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using XFLCSMS.Models.Issue;

namespace XFLCSMS.Models.Desk
{
    /// <summary>
    /// One entry in the conversation of a ticket: a reply everybody on the ticket reads, or an internal note that
    /// only AIPG staff see. Entries are only ever added - what was said stays said, with who and when - which is
    /// what the single "Comments" field of a ticket (overwritten with every edit) could not offer.
    /// </summary>
    public class TicketMessage
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [ForeignKey(nameof(Issue))]
        public int IssueId { get; set; }
        public IssueTable? Issue { get; set; }

        /// <summary>The account that wrote it. Kept as a number only: the entry stays when the account is deleted.</summary>
        public int? UserId { get; set; }

        /// <summary>Name and role of the author as they were when the entry was written.</summary>
        [MaxLength(200)]
        public string AuthorName { get; set; } = string.Empty;

        [MaxLength(40)]
        public string? AuthorRole { get; set; }

        /// <summary>Written by AIPG staff (true) or by somebody of the brokerage house (false).</summary>
        public bool FromStaff { get; set; }

        /// <summary>An internal note: shown to AIPG staff only, never to the brokerage house.</summary>
        public bool IsInternal { get; set; }

        public DateTime At { get; set; }

        /// <summary>Formatted text, cleaned by HtmlSanitizer before it is stored and again when it is shown.</summary>
        public string Body { get; set; } = string.Empty;
    }

    /// <summary>
    /// A text support staff use again and again ("We have received your ticket ...", "Please send a screenshot ...").
    /// Chosen from a list above the reply box, then edited like anything typed. {name} becomes the first name of the
    /// person who raised the ticket, {ticket} its number, {me} the name of the writer.
    /// </summary>
    public class CannedReply
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [MaxLength(120)]
        public string Title { get; set; } = string.Empty;

        public string Body { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        public DateTime UpdatedAt { get; set; }

        [MaxLength(200)]
        public string? UpdatedBy { get; set; }
    }
}
