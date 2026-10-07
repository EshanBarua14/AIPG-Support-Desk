using XFLCSMS.Models.Issue;

namespace XFLCSMS.Models.Admin
{
    /// <summary>One support engineer and the tickets he holds.</summary>
    public class EngineerLoad
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        /// <summary>Tickets assigned to him that are not closed.</summary>
        public int Open { get; set; }
        public int InProgress { get; set; }
        public int HighPriority { get; set; }
        /// <summary>When the open ticket he has had longest was assigned.</summary>
        public DateTime? OldestOpen { get; set; }
        public int ClosedLast30Days { get; set; }
    }

    /// <summary>The workload page: every engineer, and the tickets that wait for one.</summary>
    public class WorkloadView
    {
        public List<EngineerLoad> Engineers { get; set; } = new();
        public int UnassignedCount { get; set; }
        /// <summary>The tickets that have waited longest (at most 15).</summary>
        public List<IssueTable> Unassigned { get; set; } = new();
        /// <summary>Open tickets assigned to somebody who is no longer an active support engineer.</summary>
        public int Orphaned { get; set; }
    }
}
