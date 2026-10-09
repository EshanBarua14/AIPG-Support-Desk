using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace XFLCSMS.Models.Desk
{
    /// <summary>
    /// An article of the knowledge base: an answer written down once, so that the same question does not have to
    /// become a ticket every time. A published article is offered to the people of the brokerage houses while they
    /// type a ticket; an internal one is for AIPG staff only (how to diagnose, whom to ask).
    /// Maintained under Tickets > Knowledge base (permission "write the knowledge base").
    /// </summary>
    public class KbArticle
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        /// <summary>Formatted text, cleaned by HtmlSanitizer before it is stored and again when it is shown.</summary>
        public string Body { get; set; } = string.Empty;

        /// <summary>Other words people use for the same thing, separated by commas. They count in the search like the title.</summary>
        [MaxLength(300)]
        public string? Keywords { get; set; }

        /// <summary>The product the article is about; empty: every product.</summary>
        public int? ProductId { get; set; }

        /// <summary>For AIPG staff only. Never shown to, or found by, the people of a brokerage house.</summary>
        public bool IsInternal { get; set; }

        /// <summary>A draft (false) is seen by the people who write the knowledge base only.</summary>
        public bool IsPublished { get; set; }

        public int Views { get; set; }
        /// <summary>How many readers said the article answered their question, and how many said it did not.</summary>
        public int Helpful { get; set; }
        public int NotHelpful { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        [MaxLength(200)]
        public string? UpdatedBy { get; set; }

        /// <summary>The ticket the article was written from, if any (a number only: the article stays when the ticket goes).</summary>
        public int? SourceIssueId { get; set; }
    }
}
