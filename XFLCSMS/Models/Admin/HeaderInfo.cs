namespace XFLCSMS.Models.Admin
{
    public class HeaderInfo
    {
        public string? BrokerageHouseName { get; set; }
        public string? EmployeeName { get; set; }
        public int?  TotalTicket { get; set; }
        public int?  TotalCloseTicket { get; set; }
        public int?  TotalOpenTicket { get; set; }
        public int? TotalInque { get; set; }

        public string? ReportName { get; set; }

        /// <summary>Tickets of the report per status (key: the stored status).</summary>
        public Dictionary<string, int> ByStatus { get; set; } = new();

        /// <summary>The status the report was limited to, if any.</summary>
        public string? StatusName { get; set; }

        /// <summary>The product the report was limited to, if any.</summary>
        public string? ProductName { get; set; }

    }
}
