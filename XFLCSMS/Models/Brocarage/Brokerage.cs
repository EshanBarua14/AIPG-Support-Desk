using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using XFLCSMS.Models.Branch;
using XFLCSMS.Models.Issue;

namespace XFLCSMS.Models.Brocarage
{
    public class Brokerage
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int BrokerageId { get; set; }

        [Required(ErrorMessage = "The name is required.")]
        [RegularExpression(@"^[a-z A-Z.]+$", ErrorMessage = "Only letters allowed.")]
        [Display(Name = "Brokerage House Name")]
        public string BrokerageHouseName { get; set; } = string.Empty;
        [Required(ErrorMessage = "The acronym is required.")]
        [RegularExpression(@"^[a-zA-Z.]+$", ErrorMessage = "Only letters allowed.")]
        [Display(Name = "Brokerage House Acronym")]
        public string BrokerageHouseAcronym { get; set;} = string.Empty;

        public ICollection<Branchh> branches { get; set; }
        public ICollection<IssueTable> Issues { get; set; }


    }
}
