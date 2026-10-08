using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using XFLCSMS.Models.Issue;

namespace XFLCSMS.Models.Support
{
    public class SupportType
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int SupportTypeId { get; set; }
        [Required]
        [StringLength(100, MinimumLength = SupportListRules.MinimumLength, ErrorMessage = SupportListRules.LengthMessage)]
        [RegularExpression(SupportListRules.NamePattern, ErrorMessage = SupportListRules.NameMessage)]
        [Display(Name = "Support Type")]
        public string SType { get; set; }=string.Empty;
        /// <summary>The product this entry belongs to; empty: it is offered for every product.</summary>
        [Display(Name = "Product")]
        public int? ProductId { get; set; }
        public Product? Product { get; set; }
        public ICollection<IssueTable> issue { get; set; }

    }
}
