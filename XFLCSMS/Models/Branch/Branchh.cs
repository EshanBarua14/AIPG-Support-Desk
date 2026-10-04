using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using XFLCSMS.Models.Brocarage;
using XFLCSMS.Models.Issue;

namespace XFLCSMS.Models.Branch
{
    public class Branchh
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int BranchId { get; set; }

        [Required(ErrorMessage = "The name is required.")]
        [RegularExpression(@"^[a-z A-Z.]+$", ErrorMessage = "Only letters allowed.")]
        [Display(Name = "Branch Name")]
        public string BranchName { get; set; }=string.Empty;
      
        [ForeignKey("Brokerage")]
        public int? BrokerageId { get; set; }
        public Brokerage? Brokerage { get; set; }

        public ICollection<IssueTable> Issues { get; set; }

    }
} 
