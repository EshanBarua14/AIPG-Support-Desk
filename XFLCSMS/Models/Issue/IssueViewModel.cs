using System.Security.Cryptography.X509Certificates;
using XFLCSMS.Models.Affected;
using XFLCSMS.Models.Branch;
using XFLCSMS.Models.Brocarage;
using XFLCSMS.Models.Register;
using XFLCSMS.Models.Support;

namespace XFLCSMS.Models.Issue
{
    public class IssueViewModel
    {

        /// <summary>The products a ticket can be raised for (the active ones).</summary>
        public List<Product> Products { get; set; } = new();
        public List<SupportType> SupportTypes { get; set; }
        public List<SupportCatagory> SupportCatagories { get; set;}
        public List<SupportSubCatagory> SupportSubCatagories { get;set;}
        public List<AffectedSection> AffectedSections { get; set; }

        public List<Brokerage>  Brokerages { get; set; }
        public List<Branchh> Branchhs { get; set; }
        public List<string> FileLocations { get; set; }

        public IssueFrom? issueFrom { get; set; } 

        public IssueLoginInfo? LoginInfo { get; set; }
    }
}
