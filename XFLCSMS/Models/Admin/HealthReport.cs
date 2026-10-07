using XFLCSMS.Infrastructure;

namespace XFLCSMS.Models.Admin
{
    public enum HealthLevel
    {
        Ok,
        Info,
        Warning,
        Failed
    }

    /// <summary>One line of the system health page.</summary>
    public class HealthCheck
    {
        public string Area { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public HealthLevel Level { get; set; }
        /// <summary>What was found, in one sentence.</summary>
        public string Summary { get; set; } = string.Empty;
        /// <summary>What to do about it (empty when nothing needs doing).</summary>
        public string? Advice { get; set; }
        /// <summary>Action of the admin area that fixes it, with the text of the link.</summary>
        public string? LinkAction { get; set; }
        public string? LinkText { get; set; }
    }

    public class HealthReport
    {
        public DateTime At { get; set; } = DateTime.Now;
        public List<HealthCheck> Checks { get; set; } = new();
        /// <summary>Name and value: version, runtime, uptime, ...</summary>
        public List<(string Name, string Value)> Facts { get; set; } = new();
        public IReadOnlyList<RecentLog.Entry> Log { get; set; } = Array.Empty<RecentLog.Entry>();
        public string? LastMailTest { get; set; }

        public int Count(HealthLevel level) => Checks.Count(check => check.Level == level);

        public HealthLevel Overall => Checks.Count == 0 ? HealthLevel.Ok : Checks.Max(check => check.Level == HealthLevel.Info ? HealthLevel.Ok : check.Level);
    }
}
