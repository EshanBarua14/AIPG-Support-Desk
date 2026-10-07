namespace XFLCSMS.Models.Audit
{
    /// <summary>One page of the audit trail together with the filter that produced it.</summary>
    public class AuditPage
    {
        public List<AuditLog> Lines { get; set; } = new();
        public int Total { get; set; }
        public int Page { get; set; } = 1;
        public int Size { get; set; } = 50;
        public int Pages => Math.Max(1, (int)Math.Ceiling(Total / (double)Size));

        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        public string? Category { get; set; }
        public string? Q { get; set; }
        public int? House { get; set; }

        /// <summary>What this role gets to see, as a sentence for the page.</summary>
        public string Reach { get; set; } = string.Empty;
        /// <summary>The house filter is offered to the platform admin only.</summary>
        public bool CanFilterHouse { get; set; }
        public Dictionary<int, string> Houses { get; set; } = new();
        /// <summary>Categories this role can meet in its lines.</summary>
        public List<(string Key, string Name)> Categories { get; set; } = new();
    }
}
