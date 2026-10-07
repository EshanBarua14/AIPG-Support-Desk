using System.Globalization;
using Microsoft.AspNetCore.DataProtection;
using XFLCSMS.Models.Settings;

namespace XFLCSMS.Services
{
    /// <summary>
    /// The settings of the AppSettings table, kept in memory. One instance for the application: reading a setting
    /// costs no database query. The table is read again after every change made through <see cref="SaveAsync"/>
    /// and at least once a minute (for a change made directly in the database).
    /// </summary>
    public class SettingsStore
    {
        // names of the settings
        public const string RbacGrants = "rbac.grants";
        public const string DemoData = "demo.ids";

        private static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(60);
        private const string SecretPrefix = "enc:";

        private readonly IServiceScopeFactory _scopes;
        private readonly IDataProtector _protector;
        private readonly ILogger<SettingsStore> _logger;
        private readonly object _gate = new();
        private volatile Dictionary<string, string?> _values = new(StringComparer.OrdinalIgnoreCase);
        private DateTime _loadedAt = DateTime.MinValue;

        public SettingsStore(IServiceScopeFactory scopes, IDataProtectionProvider protection, ILogger<SettingsStore> logger)
        {
            _scopes = scopes;
            _protector = protection.CreateProtector("XFLCSMS.Settings.v1");
            _logger = logger;
        }

        /// <summary>Raised after settings were saved (the permission table listens).</summary>
        public event Action? Changed;

        private Dictionary<string, string?> Values
        {
            get
            {
                if (DateTime.UtcNow - _loadedAt > MaxAge)
                {
                    lock (_gate)
                    {
                        // whoever waited here while another request was reloading needs no second reload
                        if (DateTime.UtcNow - _loadedAt > MaxAge)
                        {
                            Reload();
                        }
                    }
                }

                return _values;
            }
        }

        /// <summary>Reads the table again. A database that cannot be reached keeps the values already known.</summary>
        public void Reload()
        {
            lock (_gate)
            {
                try
                {
                    using var scope = _scopes.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<DataContext>();
                    var rows = db.AppSettings.AsNoTracking().ToList();
                    var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                    foreach (var row in rows)
                    {
                        values[row.Name] = row.Value;
                    }

                    _values = values;
                    _loadedAt = DateTime.UtcNow;
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(exception, "Could not read the settings table; keeping the settings already loaded");
                    // look again in five seconds, not in a minute: until then permissions and channels are the ones
                    // known so far (after a start without database: the defaults)
                    _loadedAt = DateTime.UtcNow - MaxAge + TimeSpan.FromSeconds(5);
                }
            }

            Changed?.Invoke();
        }

        public string? Get(string name)
        {
            return Values.TryGetValue(name, out var value) && !string.IsNullOrEmpty(value) ? value : null;
        }

        public string Get(string name, string fallback)
        {
            return Get(name) ?? fallback;
        }

        public bool GetBool(string name, bool fallback)
        {
            var value = Get(name);
            return value == null ? fallback : value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        public int GetInt(string name, int fallback)
        {
            return int.TryParse(Get(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : fallback;
        }

        /// <summary>Is there a stored value (also when it is a secret that can no longer be read)?</summary>
        public bool Has(string name)
        {
            return Get(name) != null;
        }

        /// <summary>A secret in clear text, or null when there is none or it can no longer be decrypted.</summary>
        public string? GetSecret(string name)
        {
            var stored = Get(name);
            if (stored == null)
            {
                return null;
            }

            if (!stored.StartsWith(SecretPrefix, StringComparison.Ordinal))
            {
                return stored; // written by hand into the table
            }

            try
            {
                return _protector.Unprotect(stored.Substring(SecretPrefix.Length));
            }
            catch (Exception)
            {
                // the key ring was lost (App_Data/keys deleted, or the database moved to another server)
                return null;
            }
        }

        /// <summary>True when a secret is stored but cannot be decrypted any more: it has to be entered again.</summary>
        public bool SecretIsUnreadable(string name)
        {
            return Has(name) && GetSecret(name) == null;
        }

        public string Protect(string secret)
        {
            return SecretPrefix + _protector.Protect(secret);
        }

        /// <summary>The clear text of something <see cref="Protect"/> made, or null when the key for it is gone.</summary>
        public string? Reveal(string stored)
        {
            if (!stored.StartsWith(SecretPrefix, StringComparison.Ordinal))
            {
                return stored;
            }

            try
            {
                return _protector.Unprotect(stored.Substring(SecretPrefix.Length));
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Writes settings through the DataContext of the request (so an audit line added to the same context is
        /// saved in the same transaction) and reads the table again. A null or empty value removes the setting.
        /// </summary>
        public async Task SaveAsync(DataContext db, IDictionary<string, string?> values, string? by)
        {
            Stage(db, values, by);
            await db.SaveChangesAsync();
            Reload();
        }

        /// <summary>As <see cref="SaveAsync"/>, without saving: the caller saves and calls <see cref="Reload"/>.</summary>
        public void Stage(DataContext db, IDictionary<string, string?> values, string? by)
        {
            var names = values.Keys.ToList();
            var rows = db.AppSettings.Where(row => names.Contains(row.Name)).ToList();
            foreach (var pair in values)
            {
                var row = rows.FirstOrDefault(item => string.Equals(item.Name, pair.Key, StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrEmpty(pair.Value))
                {
                    if (row != null) { db.AppSettings.Remove(row); }
                    continue;
                }

                if (row == null)
                {
                    row = new AppSetting { Name = pair.Key };
                    db.AppSettings.Add(row);
                }
                else if (row.Value == pair.Value)
                {
                    continue;
                }

                row.Value = pair.Value;
                row.UpdatedOn = DateTime.Now;
                row.UpdatedBy = by;
            }
        }
    }
}
