using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using XFLCSMS.Models.Brocarage;

namespace XFLCSMS.Models.Branch
{
    public class BranchView
    {


        public int BranchId { get; set; }

        [Required(ErrorMessage = "The name is required.")]
        [RegularExpression(@"^[a-z A-Z.]+$", ErrorMessage = "Only letters allowed.")]
        [Display(Name = "Branch Name")]
        public string BranchName { get; set; } = string.Empty;


        [Display(Name = "Brokerage House Name")]
        public string BrokerageHouseName { get; set; } = string.Empty;

        [Display(Name = "Brokerage House")]
        public int BrokerageId { get; set;} 

        public List<Brokerage>? brocarage { get; set; }



    }
}
