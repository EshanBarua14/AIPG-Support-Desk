using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography.X509Certificates;

namespace XFLCSMS.Models.Issue
{
    public class Attachment
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int AttachmentId { get; set; } 
        public string AttachmentLoc { get; set; } = string.Empty;
        public string FileName { get; set; }= string.Empty;
        [Required]
        [ForeignKey("issue")]
        public int IssueId { get; set; }
        public IssueTable issue { get; set; }

        /// <summary>The conversation entry the file came with; null for the files of the ticket itself.</summary>
        public long? MessageId { get; set; }

    }
}
