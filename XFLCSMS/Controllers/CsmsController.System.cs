using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Newtonsoft.Json;
using XFLCSMS.Infrastructure;
using XFLCSMS.Models.Admin;
using XFLCSMS.Models.Affected;
using XFLCSMS.Models.Branch;
using XFLCSMS.Models.Brocarage;
using XFLCSMS.Models.Common;
using XFLCSMS.Models.Issue;
using XFLCSMS.Models.Register;
using XFLCSMS.Models.Support;
using XFLCSMS.Models.Todos;
using XFLCSMS.Models.Notify;
using XFLCSMS.Services;
using XFLCSMS.Services.EmailService;
using XFLCSMS.Services.Notify;

namespace XFLCSMS.Controllers
{
    // Pages about the system itself: health, what each role may do, notification settings, demo data.
    public abstract partial class CsmsController
    {
        // ---- system health -------------------------------------------------------------------------

        /// <summary>Is everything the system needs working? Database, e-mail, files, settings, and the support queue.</summary>
        [HttpGet]
        [RequirePermission(Permission.SystemHealth)]
        public async Task<IActionResult> SystemHealth()
        {
            try
            {
                var health = HttpContext.RequestServices.GetRequiredService<SystemHealthService>();
                return View(await health.BuildAsync());
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        /// <summary>Signs in to the mail server (nothing is sent) and shows whether that worked.</summary>
        [HttpPost]
        [RequirePermission(Permission.SystemHealth)]
        public async Task<IActionResult> TestMail()
        {
            try
            {
                var health = HttpContext.RequestServices.GetRequiredService<SystemHealthService>();
                var result = health.TestMail();
                Audit(AuditActions.SystemMailTest, "System", null, "Mail server", result.Ok ? "The mail server accepted the sign-in" : result.Message, null);
                await Db.SaveChangesAsync();
                TempData[result.Ok ? "SuccessMessage" : "ErrorMessage"] = result.Message;
                return RedirectToAction("SystemHealth");
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        // ---- roles and permissions -------------------------------------------------------------------

        private SettingsStore Settings => HttpContext.RequestServices.GetRequiredService<SettingsStore>();

        /// <summary>What each role may do, as a table of switches.</summary>
        [HttpGet]
        [RequirePermission(Permission.PermissionsEdit)]
        public async Task<IActionResult> Permissions()
        {
            try
            {
                var people = (await Db.Users.Where(u => u.UStatus).Select(u => new { u.UCatagory, u.UType, u.Department }).ToListAsync())
                    .GroupBy(u => Rbac.RoleOf(u.UCatagory, u.UType, u.Department))
                    .ToDictionary(group => group.Key, group => group.Count());
                var row = await Db.AppSettings.AsNoTracking().FirstOrDefaultAsync(item => item.Name == SettingsStore.RbacGrants);
                return View(new PermissionsView { IsCustomised = Rbac.IsCustomised, People = people, ChangedOn = row?.UpdatedOn, ChangedBy = row?.UpdatedBy });
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        /// <param name="grants">One "Role:Permission" for every switch that is on.</param>
        [HttpPost]
        [RequirePermission(Permission.PermissionsEdit)]
        public async Task<IActionResult> SavePermissions(List<string>? grants, bool reset = false)
        {
            try
            {
                Dictionary<Role, IEnumerable<Permission>>? chosen = null;
                if (!reset)
                {
                    var picked = Rbac.AllRoles.ToDictionary(role => role, role => new List<Permission>());
                    foreach (var grant in grants ?? new List<string>())
                    {
                        var parts = grant.Split(':', 2);
                        if (parts.Length == 2 && Enum.TryParse<Role>(parts[0], out var role) && Enum.IsDefined(role)
                            && Enum.TryParse<Permission>(parts[1], out var permission) && Enum.IsDefined(permission))
                        {
                            picked[role].Add(permission);
                        }
                    }

                    chosen = picked.ToDictionary(pair => pair.Key, pair => (IEnumerable<Permission>)pair.Value);
                }

                var next = Rbac.Preview(chosen);
                var changes = new List<string>();
                foreach (var role in Rbac.AllRoles)
                {
                    var before = Rbac.PermissionsOf(role);
                    var added = next.Table[role].Where(permission => !before.Contains(permission)).Select(Rbac.NameOf).ToList();
                    var removed = before.Where(permission => !next.Table[role].Contains(permission)).Select(Rbac.NameOf).ToList();
                    if (added.Count + removed.Count > 0)
                    {
                        changes.Add(Rbac.Label(role) + ": "
                            + string.Join(", ", added.Select(name => "+ " + name).Concat(removed.Select(name => "\u2212 " + name))));
                    }
                }

                if (changes.Count == 0)
                {
                    TempData["SuccessMessage"] = "Nothing was changed.";
                    return RedirectToAction("Permissions");
                }

                var me = CurrentUser!;
                Audit(AuditActions.SystemPermissions, "System", null, "Roles and permissions",
                    (reset ? "Reset to the defaults. " : string.Empty) + string.Join("; ", changes), null);
                // the defaults are not stored: a later version with new permissions then gives them their default owners
                await Settings.SaveAsync(Db, new Dictionary<string, string?> { [SettingsStore.RbacGrants] = next.IsDefault ? null : next.Text }, me.FullName);

                TempData["SuccessMessage"] = reset
                    ? "The permissions are back to the defaults."
                    : "Saved. The change is in force now, also for people who are signed in.";
                return RedirectToAction("Permissions");
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        // ---- notification settings -------------------------------------------------------------------

        [HttpGet]
        [RequirePermission(Permission.SystemSettings)]
        public async Task<IActionResult> NotificationSettings()
        {
            try
            {
                var settings = Settings;
                var me = CurrentUser!;
                var view = new NotificationSettingsView
                {
                    InApp = NotificationEvents.ChannelOn(settings, NotificationEvents.InAppChannel),
                    Email = NotificationEvents.ChannelOn(settings, NotificationEvents.EmailChannel),
                    Sms = NotificationEvents.ChannelOn(settings, NotificationEvents.SmsChannel),
                    SiteUrl = settings.Get(NotificationService.SiteUrlKey),
                    RequestUrl = Request.Scheme + "://" + Request.Host + Request.PathBase,
                    Mail = HttpContext.RequestServices.GetRequiredService<IEmailServices>().Settings,
                    MailHasPassword = settings.Has(MailSettings.PasswordKey),
                    SmsUrl = settings.Get(SmsSender.UrlKey, string.Empty),
                    SmsMethod = settings.Get(SmsSender.MethodKey, "POST"),
                    SmsContentType = settings.Get(SmsSender.ContentTypeKey, "form"),
                    SmsBody = settings.Get(SmsSender.BodyKey, string.Empty),
                    SmsHeader = settings.Get(SmsSender.HeaderKey, string.Empty),
                    SmsSender = settings.Get(SmsSender.SenderKey, string.Empty),
                    SmsPrefix = settings.Get(SmsSender.PrefixKey, string.Empty),
                    SmsSuccess = settings.Get(SmsSender.SuccessKey, string.Empty),
                    SmsHasKey = settings.Has(SmsSender.ApiKeyKey),
                    SmsKeyUnreadable = settings.SecretIsUnreadable(SmsSender.ApiKeyKey),
                    SmsConfigured = HttpContext.RequestServices.GetRequiredService<SmsSender>().IsConfigured,
                    Queued = await Db.NotificationDeliveries.CountAsync(item => item.Status == NotificationDelivery.Queued),
                    Failed = await Db.NotificationDeliveries.CountAsync(item => item.Status == NotificationDelivery.Failed),
                    Recent = await Db.NotificationDeliveries.OrderByDescending(item => item.Id).Take(25).ToListAsync(),
                    WorkerLastRun = HttpContext.RequestServices.GetRequiredService<NotificationWorker>().LastRun,
                    MyEmail = me.Email,
                    MyPhone = me.PhonNumber
                };

                foreach (var item in NotificationEvents.All)
                {
                    foreach (var channel in new[] { NotificationEvents.InAppChannel, NotificationEvents.EmailChannel, NotificationEvents.SmsChannel })
                    {
                        if (NotificationEvents.EventOn(settings, item.Key, channel)) { view.On.Add(item.Key + ":" + channel); }
                    }
                }

                return View(view);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        /// <summary>The switches: channels, and which event uses which channel.</summary>
        /// <param name="on">"ticket.created:email" for every event switch that is on.</param>
        [HttpPost]
        [RequirePermission(Permission.SystemSettings)]
        public async Task<IActionResult> SaveNotificationSwitches(bool inApp, bool email, bool sms, string? siteUrl, List<string>? on)
        {
            try
            {
                siteUrl = (siteUrl ?? string.Empty).Trim().TrimEnd('/');
                if (siteUrl.Length > 0 && !(Uri.TryCreate(siteUrl, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)))
                {
                    TempData["ErrorMessage"] = "The site address must start with http:// or https://, for example https://csms.example.com.";
                    return RedirectToAction("NotificationSettings");
                }

                var chosen = new HashSet<string>(on ?? new List<string>(), StringComparer.Ordinal);
                var values = new Dictionary<string, string?>
                {
                    [NotificationEvents.ChannelKey(NotificationEvents.InAppChannel)] = inApp ? "1" : "0",
                    [NotificationEvents.ChannelKey(NotificationEvents.EmailChannel)] = email ? "1" : "0",
                    [NotificationEvents.ChannelKey(NotificationEvents.SmsChannel)] = sms ? "1" : "0",
                    [NotificationService.SiteUrlKey] = siteUrl
                };
                foreach (var item in NotificationEvents.All)
                {
                    foreach (var channel in new[] { NotificationEvents.InAppChannel, NotificationEvents.EmailChannel, NotificationEvents.SmsChannel })
                    {
                        values[NotificationEvents.EventKey(item.Key, channel)] = chosen.Contains(item.Key + ":" + channel) ? "1" : "0";
                    }
                }

                var settings = Settings;
                var changed = values.Where(pair => (settings.Get(pair.Key) ?? string.Empty) != (pair.Value ?? string.Empty)).Select(pair => pair.Key).ToList();
                // a switch that was never stored and is left at its default is no change
                changed = changed.Where(key => !IsDefaultSwitch(key, values[key])).ToList();
                if (changed.Count > 0)
                {
                    Audit(AuditActions.SystemSettings, "System", null, "Notifications",
                        "Changed the notification switches: " + string.Join(", ", changed.Take(12).Select(key => DescribeSwitch(key, values[key]))) + (changed.Count > 12 ? ", \u2026" : string.Empty), null);
                }

                await settings.SaveAsync(Db, values, CurrentUser!.FullName);
                TempData["SuccessMessage"] = sms && !HttpContext.RequestServices.GetRequiredService<SmsSender>().IsConfigured
                    ? "Saved. SMS is switched on, but no gateway is set up yet: fill in the SMS card below."
                    : "Saved.";
                return RedirectToAction("NotificationSettings");
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        private bool IsDefaultSwitch(string key, string? value)
        {
            if (Settings.Has(key) || key == NotificationService.SiteUrlKey) { return false; }
            foreach (var channel in new[] { NotificationEvents.InAppChannel, NotificationEvents.EmailChannel, NotificationEvents.SmsChannel })
            {
                if (key == NotificationEvents.ChannelKey(channel)) { return (value == "1") == NotificationEvents.ChannelOn(Settings, channel); }
                foreach (var item in NotificationEvents.All)
                {
                    if (key == NotificationEvents.EventKey(item.Key, channel)) { return (value == "1") == NotificationEvents.EventOn(Settings, item.Key, channel); }
                }
            }

            return false;
        }

        private static string DescribeSwitch(string key, string? value)
        {
            if (key == NotificationService.SiteUrlKey) { return "site address \u201c" + value + "\u201d"; }
            var state = value == "1" ? " on" : " off";
            foreach (var channel in new[] { (NotificationEvents.InAppChannel, "in the application"), (NotificationEvents.EmailChannel, "e-mail"), (NotificationEvents.SmsChannel, "SMS") })
            {
                if (key == NotificationEvents.ChannelKey(channel.Item1)) { return "all " + channel.Item2 + state; }
                foreach (var item in NotificationEvents.All)
                {
                    if (key == NotificationEvents.EventKey(item.Key, channel.Item1)) { return "\u201c" + item.Name + "\u201d by " + channel.Item2 + state; }
                }
            }

            return key + state;
        }

        [HttpPost]
        [RequirePermission(Permission.SystemSettings)]
        public async Task<IActionResult> SaveMailSettings(string? host, int port, string? security, string? user, string? password, string? from, string? fromName, bool useFile = false)
        {
            try
            {
                var me = CurrentUser!;
                var settings = Settings;
                if (useFile)
                {
                    // forget what the page stored: the three values of appsettings.json are used again
                    await ClearAsync(MailSettings.HostKey, MailSettings.PortKey, MailSettings.SecurityKey, MailSettings.UserKey, MailSettings.PasswordKey, MailSettings.FromKey, MailSettings.FromNameKey);
                    Audit(AuditActions.SystemSettings, "System", null, "Mail server", "Removed the mail server entered here; appsettings.json is used again", null);
                    await Db.SaveChangesAsync();
                    TempData["SuccessMessage"] = "The mail server of appsettings.json is used again.";
                    return RedirectToAction("NotificationSettings");
                }

                host = (host ?? string.Empty).Trim();
                user = (user ?? string.Empty).Trim();
                from = (from ?? string.Empty).Trim();
                fromName = (fromName ?? string.Empty).Trim();
                security = new[] { "auto", "starttls", "ssl", "none" }.Contains(security) ? security : "auto";
                if (from.Length == 0) { from = user; }

                string? problem = null;
                if (host.Length == 0 || host.Contains(' ') || host.Contains('/')) { problem = "Enter the name of the mail server, for example smtp.gmail.com."; }
                else if (port < 1 || port > 65535) { problem = "The port is a number from 1 to 65535 (usually 587 or 465)."; }
                else if (!new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(from) || from.Length == 0) { problem = "Enter the address the messages come from (an e-mail address)."; }
                else if (host.Length > 200 || user.Length > 200 || from.Length > 200 || fromName.Length > 100 || (password ?? string.Empty).Length > 200) { problem = "One of the values is too long."; }
                // The stored password is only ever sent to the server and the account it was entered for. Somebody
                // who changes either has to know the password.
                var storedHost = settings.Get(MailSettings.HostKey);
                var keepsPassword = string.IsNullOrEmpty(password) && user.Length > 0 && settings.Has(MailSettings.PasswordKey);
                if (problem == null && keepsPassword
                    && (!string.Equals(storedHost, host, StringComparison.OrdinalIgnoreCase) || !string.Equals(settings.Get(MailSettings.UserKey, string.Empty), user, StringComparison.OrdinalIgnoreCase)))
                {
                    problem = "Enter the password again: the server or the user name changed, and the stored password belongs to the old ones.";
                }
                else if (problem == null && string.IsNullOrEmpty(password) && user.Length > 0 && !settings.Has(MailSettings.PasswordKey))
                {
                    problem = "Enter the password for " + user + ". (Leave the user name empty for a server that needs no sign-in.)";
                }

                if (problem != null)
                {
                    TempData["ErrorMessage"] = problem;
                    return RedirectToAction("NotificationSettings");
                }

                var values = new Dictionary<string, string?>
                {
                    [MailSettings.HostKey] = host,
                    [MailSettings.PortKey] = port.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    [MailSettings.SecurityKey] = security,
                    [MailSettings.UserKey] = user,
                    [MailSettings.FromKey] = from,
                    [MailSettings.FromNameKey] = fromName
                };
                // the password field is always empty when the page opens: empty means "keep what is stored"
                if (!string.IsNullOrEmpty(password))
                {
                    values[MailSettings.PasswordKey] = settings.Protect(password);
                }
                else if (user.Length == 0)
                {
                    values[MailSettings.PasswordKey] = null; // a server without sign-in needs no password
                }

                Audit(AuditActions.SystemSettings, "System", null, "Mail server",
                    "Saved the mail server " + host + ":" + port + " (" + security + "), sender " + from
                    + (string.IsNullOrEmpty(password) ? string.Empty : ", new password"), null);
                await settings.SaveAsync(Db, values, me.FullName);
                TempData["SuccessMessage"] = "The mail server is saved. Send a test message to see that it works.";
                return RedirectToAction("NotificationSettings");
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        private async Task ClearAsync(params string[] keys)
        {
            Settings.Stage(Db, keys.ToDictionary(key => key, key => (string?)null), CurrentUser!.FullName);
            await Db.SaveChangesAsync();
            Settings.Reload();
        }

        [HttpPost]
        [RequirePermission(Permission.SystemSettings)]
        public async Task<IActionResult> SaveSmsSettings(string? url, string? method, string? contentType, string? body, string? header, string? apiKey, string? sender, string? prefix, string? success, bool clear = false)
        {
            try
            {
                var settings = Settings;
                if (clear)
                {
                    await ClearAsync(SmsSender.UrlKey, SmsSender.MethodKey, SmsSender.ContentTypeKey, SmsSender.BodyKey, SmsSender.HeaderKey, SmsSender.ApiKeyKey, SmsSender.SenderKey, SmsSender.PrefixKey, SmsSender.SuccessKey);
                    Audit(AuditActions.SystemSettings, "System", null, "SMS gateway", "Removed the SMS gateway", null);
                    await Db.SaveChangesAsync();
                    TempData["SuccessMessage"] = "The SMS gateway is removed.";
                    return RedirectToAction("NotificationSettings");
                }

                url = (url ?? string.Empty).Trim();
                method = string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) ? "GET" : "POST";
                contentType = contentType == "json" ? "json" : "form";
                body = (body ?? string.Empty).Trim();
                header = (header ?? string.Empty).Trim();
                sender = (sender ?? string.Empty).Trim();
                prefix = new string((prefix ?? string.Empty).Where(char.IsDigit).ToArray());
                success = (success ?? string.Empty).Trim();

                var probe = url.Replace("{", string.Empty).Replace("}", string.Empty);
                var whole = url + " " + (method == "POST" ? body : string.Empty);
                string? problem = null;
                if (!(Uri.TryCreate(probe, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)))
                {
                    problem = "Enter the address of the gateway. It starts with http:// or https://.";
                }
                else if (!whole.Contains("{to}") || !whole.Contains("{message}"))
                {
                    problem = "The address or the request body must contain {to} and {message}: that is where the number and the text go.";
                }
                else if (header.Length > 0 && !header.Contains(':'))
                {
                    problem = "Write the extra header as Name: value, for example Authorization: Bearer {key}.";
                }
                else if (url.Length > 1000 || body.Length > 2000 || header.Length > 500 || sender.Length > 50 || success.Length > 100 || (apiKey ?? string.Empty).Length > 500)
                {
                    problem = "One of the values is too long.";
                }

                // the stored key is only ever sent to the gateway it was entered for
                if (problem == null && string.IsNullOrEmpty(apiKey) && settings.Has(SmsSender.ApiKeyKey))
                {
                    var before = settings.Get(SmsSender.UrlKey, string.Empty).Replace("{", string.Empty).Replace("}", string.Empty);
                    if (!Uri.TryCreate(before, UriKind.Absolute, out var old) || !string.Equals(old.Host, uri!.Host, StringComparison.OrdinalIgnoreCase)
                        || old.Port != uri.Port || old.Scheme != uri.Scheme)
                    {
                        problem = "Enter the key again: the address of the gateway changed, and the stored key belongs to the old one.";
                    }
                }

                if (problem != null)
                {
                    TempData["ErrorMessage"] = problem;
                    return RedirectToAction("NotificationSettings");
                }

                var values = new Dictionary<string, string?>
                {
                    [SmsSender.UrlKey] = url,
                    [SmsSender.MethodKey] = method,
                    [SmsSender.ContentTypeKey] = contentType,
                    [SmsSender.BodyKey] = body,
                    [SmsSender.HeaderKey] = header,
                    [SmsSender.SenderKey] = sender,
                    [SmsSender.PrefixKey] = prefix,
                    [SmsSender.SuccessKey] = success
                };
                if (!string.IsNullOrEmpty(apiKey))
                {
                    values[SmsSender.ApiKeyKey] = settings.Protect(apiKey.Trim());
                }

                Audit(AuditActions.SystemSettings, "System", null, "SMS gateway",
                    "Saved the SMS gateway " + uri!.Host + " (" + method + ")" + (string.IsNullOrEmpty(apiKey) ? string.Empty : ", new key"), null);
                await settings.SaveAsync(Db, values, CurrentUser!.FullName);
                TempData["SuccessMessage"] = "The SMS gateway is saved. Send a test message to see that it works.";
                return RedirectToAction("NotificationSettings");
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        /// <summary>Sends one real message at once (not through the queue) and shows whether the server took it.</summary>
        [HttpPost]
        [RequirePermission(Permission.SystemSettings)]
        public async Task<IActionResult> SendTestMessage(string channel, string? to)
        {
            try
            {
                var me = CurrentUser!;
                to = (to ?? string.Empty).Trim();
                string? error;
                string what;
                if (channel == NotificationDelivery.Sms)
                {
                    what = "SMS";
                    if (to.Length == 0) { to = me.PhonNumber; }
                    error = await HttpContext.RequestServices.GetRequiredService<SmsSender>()
                        .SendAsync(to, "XFL CSMS: this is a test message. SMS notifications work.", HttpContext.RequestAborted);
                }
                else
                {
                    what = "e-mail";
                    if (to.Length == 0) { to = me.Email; }
                    if (!new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(to))
                    {
                        error = "\u201c" + to + "\u201d is not an e-mail address.";
                    }
                    else
                    {
                        try
                        {
                            var notify = HttpContext.RequestServices.GetRequiredService<NotificationService>();
                            HttpContext.RequestServices.GetRequiredService<IEmailServices>().SendEmail(new XFLCSMS.Models.Email.EmailDto
                            {
                                To = to,
                                Subject = "XFL CSMS: test message",
                                Body = notify.MailBody(me, "This is a test message", "E-mail notifications of XFL CSMS work. Sent by " + me.FullName + ".", "NotificationSettings", null)
                            });
                            error = null;
                        }
                        catch (Exception exception)
                        {
                            error = XFLCSMS.EmailService.EmailService.Describe(exception);
                        }
                    }
                }

                Audit(channel == NotificationDelivery.Sms ? AuditActions.SystemSmsTest : AuditActions.SystemMailTest, "System", null,
                    channel == NotificationDelivery.Sms ? "SMS gateway" : "Mail server",
                    error == null ? "Sent a test " + what + " to " + to : "Test " + what + " to " + to + " failed: " + error, null);
                await Db.SaveChangesAsync();

                if (error == null)
                {
                    TempData["SuccessMessage"] = "The test " + what + " to " + to + " was accepted. Check that it arrives.";
                }
                else
                {
                    TempData["ErrorMessage"] = "The test " + what + " was not sent. " + error;
                }

                return RedirectToAction("NotificationSettings");
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        /// <summary>Puts failed messages (one, or all) back in the queue.</summary>
        [HttpPost]
        [RequirePermission(Permission.SystemSettings)]
        public async Task<IActionResult> RetryDeliveries(long? id)
        {
            try
            {
                var failed = await Db.NotificationDeliveries
                    .Where(item => item.Status == NotificationDelivery.Failed && (id == null || item.Id == id))
                    .OrderBy(item => item.Id).Take(500).ToListAsync();
                foreach (var item in failed)
                {
                    item.Status = NotificationDelivery.Queued;
                    item.Attempts = 0;
                    item.NextTryAt = null;
                    item.DoneAt = null;
                }

                await Db.SaveChangesAsync();
                HttpContext.RequestServices.GetRequiredService<NotificationWorker>().Wake();
                TempData["SuccessMessage"] = failed.Count == 0 ? "There is nothing to send again."
                    : failed.Count == 1 ? "The message is in the queue again." : failed.Count + " messages are in the queue again.";
                return RedirectToAction("NotificationSettings");
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        // ---- demo data -----------------------------------------------------------------------------

        [HttpGet]
        [RequirePermission(Permission.SystemSettings)]
        public async Task<IActionResult> DemoData()
        {
            try
            {
                return View(await HttpContext.RequestServices.GetRequiredService<DemoDataService>().StatusAsync());
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [HttpPost]
        [RequirePermission(Permission.SystemSettings)]
        public async Task<IActionResult> LoadDemoData(bool besideRealData = false)
        {
            try
            {
                var demo = HttpContext.RequestServices.GetRequiredService<DemoDataService>();
                var result = await demo.LoadAsync(CurrentUser!, besideRealData);
                TempData[result.Ok ? "SuccessMessage" : "ErrorMessage"] = result.Message;
                return RedirectToAction("DemoData");
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [HttpPost]
        [RequirePermission(Permission.SystemSettings)]
        public async Task<IActionResult> RemoveDemoData()
        {
            try
            {
                var demo = HttpContext.RequestServices.GetRequiredService<DemoDataService>();
                var result = await demo.RemoveAsync(CurrentUser!);
                TempData[result.Ok ? "SuccessMessage" : "ErrorMessage"] = result.Message;
                return RedirectToAction("DemoData");
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }
    }
}
