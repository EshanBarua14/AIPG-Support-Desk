using XFLCSMS.Models.Register;
using XFLCSMS.Models.Brocarage;

namespace XFLCSMS.Models.Admin
{
    public class ReportView
    {
        public ICollection<string>? EmployeeNames { get; set; }
        public ICollection<Brokerage>? brocarages { get; set; }
        public Search? search { get; set; }
        public HeaderInfo? HeaderInfo { get; set; }
        public SESearch? SESearch { get; set; }

        public MakerSearch? MakerSearch { get; set; }

        public IEnumerable<XFLCSMS.Models.Issue.IssueTable>? Issues { get; set; }

        /// <summary>The products: the choices of the filter, and the names for the result.</summary>
        public List<XFLCSMS.Models.Support.Product> Products { get; set; } = new();

        /// <summary>The numbers behind the charts of the result; null when the report found no ticket.</summary>
        public TicketCharts? Charts { get; set; }
    }
}
