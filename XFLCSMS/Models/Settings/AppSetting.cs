using System.ComponentModel.DataAnnotations;

namespace XFLCSMS.Models.Settings
{
    /// <summary>
    /// One setting that is changed from the pages of the application instead of appsettings.json:
    /// what each role may do, the mail server, the SMS gateway, which event tells whom.
    /// Read and written through Services/SettingsStore.cs.
    /// </summary>
    public class AppSetting
    {
        [Key]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        /// <summary>Secrets (mail password, SMS key) are stored encrypted, see SettingsStore.Protect.</summary>
        public string? Value { get; set; }

        public DateTime UpdatedOn { get; set; }

        [StringLength(200)]
        public string? UpdatedBy { get; set; }
    }
}
