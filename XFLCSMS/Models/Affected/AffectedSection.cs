using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using XFLCSMS.Models.Issue;

namespace XFLCSMS.Models.Affected
{
    public class AffectedSection
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int AffectedSectionId { get; set; }
        [Required]
        //[StringLength(100, MinimumLength = 4, ErrorMessage = "Password must be 4 to 100 characters")]
        //[RegularExpression(@"^[a-zA-Z.]+$", ErrorMessage = "Only letters allowed.")]
        [Display(Name = "Affected Section")]
        public string ASection { get; set; } = string.Empty;

        public ICollection<IssueTable> issue { get; set; }
    }
}
