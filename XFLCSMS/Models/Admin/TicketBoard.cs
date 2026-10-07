using XFLCSMS.Models.Issue;
using XFLCSMS.Services;

namespace XFLCSMS.Models.Admin
{
    /// <summary>The board: one column per status with the tickets in it (most urgent first).</summary>
    public class TicketBoardView
    {
        public List<TicketBoardColumn> Columns { get; set; } = new();
        /// <summary>Only the tickets assigned to the signed-in engineer.</summary>
        public bool Mine { get; set; }
        public bool CanFilterMine { get; set; }
    }

    public class TicketBoardColumn
    {
        public TicketStatus.Info Status { get; set; } = TicketStatus.All[0];
        /// <summary>All tickets with this status; <see cref="Tickets"/> holds the first few.</summary>
        public int Total { get; set; }
        public List<IssueTable> Tickets { get; set; } = new();
    }
}
