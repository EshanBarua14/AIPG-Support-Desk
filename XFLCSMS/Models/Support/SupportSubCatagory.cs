using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using XFLCSMS.Models.Issue;

namespace XFLCSMS.Models.Support
{
    public class SupportSubCatagory
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int SupportSubCatagoryId { get; set; }
        [Required]
        [StringLength(100, MinimumLength = 4, ErrorMessage = "The name must be 4 to 100 characters long.")]
        [RegularExpression(@"^[a-zA-Z.]+$", ErrorMessage = "Only letters allowed.")]
        public string SubCatagory { get; set; } = string.Empty;
        public ICollection<IssueTable> issue { get; set; }
    }
}
