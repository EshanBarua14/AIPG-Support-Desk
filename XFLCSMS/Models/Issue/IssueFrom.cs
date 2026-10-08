using System.ComponentModel.DataAnnotations;

namespace XFLCSMS.Models.Issue
{
    public class IssueFrom
    {
        public int UserId { get; set; }
        public string BrocarageHouseName { get; set; }
        public string BranchName { get; set; }


        public string TicketId { get; set; } = string.Empty;
        public DateTime dateTime { get; set; }
        [Required(ErrorMessage ="Title I required")]
        public string ITitle { get; set; } = string.Empty;

        public int? ProductId { get; set; }
        public int? SupportTypeId { get; set; }
        public int? SupportCatagoryId { get; set; }
        public int? SupportSubCatagoryID { get; set; }
        public int? AffectedSectionId { get; set; }
        [Required(ErrorMessage ="Please Select A Priority")]
        public string? Priority { get; set; }= string.Empty;
        public string? IssueDetails { get; set; } 
        public string? Commands { get; set; }


    }
}
