using XFLCSMS.Infrastructure;

namespace XFLCSMS.Models.Admin
{
    /// <summary>The "Roles and permissions" page: what each role may do, as a table of switches.</summary>
    public class PermissionsView
    {
        public bool IsCustomised { get; set; }
        /// <summary>People per role, so the effect of a change can be judged.</summary>
        public Dictionary<Role, int> People { get; set; } = new();
        public DateTime? ChangedOn { get; set; }
        public string? ChangedBy { get; set; }
    }
}
