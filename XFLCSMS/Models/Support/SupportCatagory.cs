using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using XFLCSMS.Models.Issue;

namespace XFLCSMS.Models.Support
{
    public class SupportCatagory
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int SupportCatagoryId { get; set; }
        [Required]
        [StringLength(100, MinimumLength = 4, ErrorMessage = "The name must be 4 to 100 characters long.")]
        [RegularExpression(@"^[a-zA-Z.]+$", ErrorMessage = "Only letters allowed.")]
        [Display(Name = "Support Category")]
        public string SCatagory { get; set; } = string.Empty;
        public ICollection<IssueTable> issue { get; set; }

    }
}
