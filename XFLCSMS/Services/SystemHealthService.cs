using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using XFLCSMS.Infrastructure;
using XFLCSMS.Models.Admin;
using XFLCSMS.Services.EmailService;

namespace XFLCSMS.Services
{
    /// <summary>
    /// The checks behind the system health page: database, e-mail, file storage, security settings and whether
    /// the support work itself is stuck. Every check catches its own errors: a broken part must show up as
    /// "failed" on the page, not break the page.
    /// </summary>
    public class SystemHealthService
    {
        private readonly DataContext _context;
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _environment;
        private readonly TicketService _tickets;
        private readonly IEmailServices _mail;
        private readonly IServer _server;
        private readonly IHttpContextAccessor _http;

        /// <summary>Result of the last "Test mail server" (kept in memory until the application restarts).</summary>
        private static string? _lastMailTest;
        private static bool? _lastMailOk;

        public SystemHealthService(DataContext context, IConfiguration configuration, IWebHostEnvironment environment,
            TicketService tickets, IEmailServices mail, IServer server, IHttpContextAccessor http)
        {
            _context = context;
            _configuration = configuration;
            _environment = environment;
            _tickets = tickets;
            _mail = mail;
            _server = server;
            _http = http;
        }

        /// <summary>The answer for monitoring tools: can the application reach its database?</summary>
        public async Task<bool> IsAliveAsync()
        {
            try
            {
                return await _context.Database.CanConnectAsync();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Signs in to the mail server without sending anything. Returns the sentence shown to the administrator.</summary>
        public (bool Ok, string Message) TestMail()
        {
            var error = _mail.TestConnection();
            _lastMailOk = error == null;
            _lastMailTest = DateTime.Now.ToString("dd MMM yyyy, h:mm tt") + ": " + (error == null
                ? "the mail server accepted the sign-in."
                : "failed. " + error);
            return (error == null, error == null
                ? "The mail server accepted the sign-in. Registration and password e-mails can be sent."
                : "The mail server test failed: " + error);
        }

        public async Task<HealthReport> BuildAsync()
        {
            var report = new HealthReport { LastMailTest = _lastMailTest };
            var dbUp = await DatabaseAsync(report);
            Mail(report);
            if (dbUp)
            {
                await NotificationsAsync(report);
            }

            await StorageAsync(report, dbUp);
            await SecurityAsync(report, dbUp);
            if (dbUp)
            {
                await OperationsAsync(report);
            }

            Facts(report);
            report.Log = RecentLog.Instance.Entries.Take(50).ToList();
            return report;
        }

        private static void Add(HealthReport report, string area, string name, HealthLevel level, string summary, string? advice = null, string? linkAction = null, string? linkText = null)
        {
            report.Checks.Add(new HealthCheck { Area = area, Name = name, Level = level, Summary = summary, Advice = advice, LinkAction = linkAction, LinkText = linkText });
        }

        // ---- database --------------------------------------------------------------------------

        private async Task<bool> DatabaseAsync(HealthReport report)
        {
            const string area = "Database";
            try
            {
                var watch = Stopwatch.StartNew();
                var users = await _context.Users.CountAsync();
                watch.Stop();
                var ms = watch.ElapsedMilliseconds;
                Add(report, area, "Connection", ms > 2000 ? HealthLevel.Warning : HealthLevel.Ok,
                    "The database answers (" + ms + " ms).",
                    ms > 2000 ? "A simple question took more than two seconds. Check the load on the database server." : null);

                var tickets = await _context.Issues.CountAsync();
                var files = await _context.Attachments.CountAsync();
                var audit = await _context.AuditLogs.CountAsync();
                Add(report, area, "Content", HealthLevel.Info,
                    users.ToString("N0") + " accounts, " + tickets.ToString("N0") + " tickets, " + files.ToString("N0") + " attached files, " + audit.ToString("N0") + " audit lines.");
            }
            catch (Exception exception)
            {
                Add(report, area, "Connection", HealthLevel.Failed, "The database does not answer: " + Short(exception),
                    "Check that SQL Server runs and ConnectionStrings:DefaultConnection in appsettings.json.");
                return false;
            }

            try
            {
                var pending = (await _context.Database.GetPendingMigrationsAsync()).ToList();
                if (pending.Count == 0)
                {
                    Add(report, area, "Structure", HealthLevel.Ok, "The tables match this version of the application.");
                }
                else
                {
                    Add(report, area, "Structure", HealthLevel.Failed, pending.Count + " database update(s) are not applied: " + string.Join(", ", pending) + ".",
                        "Restart the application: it applies them at start-up (unless Database:AutoMigrate is switched off).");
                }
            }
            catch (Exception exception)
            {
                Add(report, area, "Structure", HealthLevel.Warning, "Could not compare the tables with this version: " + Short(exception));
            }

            return true;
        }

        // ---- e-mail ----------------------------------------------------------------------------

        private void Mail(HealthReport report)
        {
            const string area = "E-mail";
            var settings = _mail.Settings;
            var host = settings.Host;
            var sender = settings.From + (settings.Source == "file" ? " (from appsettings.json)" : string.Empty);

            if (settings.PasswordUnreadable)
            {
                Add(report, area, "Settings", HealthLevel.Failed, "The stored mail password can no longer be read.",
                    "The key folder App_Data/keys was replaced or lost. Enter the password again.", "NotificationSettings", "Notification settings");
                return;
            }

            if (!settings.IsConfigured || (settings.User.Length > 0 && settings.Password.Length == 0))
            {
                Add(report, area, "Settings", HealthLevel.Warning, "The mail server is not fully set up.",
                    "Registration tokens, password resets and e-mail notifications cannot be sent. People can still get in: you create or activate their accounts under Users.",
                    "NotificationSettings", "Notification settings");
                return;
            }

            if (_lastMailOk == null)
            {
                Add(report, area, "Mail server", HealthLevel.Info, "Sends through " + host + " as " + sender + ". Not tested since the application started.",
                    "Press “Test mail server” to see whether it accepts the sign-in.");
            }
            else if (_lastMailOk == true)
            {
                Add(report, area, "Mail server", HealthLevel.Ok, "Sends through " + host + " as " + sender + ". Last test, " + _lastMailTest);
            }
            else
            {
                Add(report, area, "Mail server", HealthLevel.Failed, "Sends through " + host + " as " + sender + ". Last test, " + _lastMailTest,
                    "Registration tokens and password resets do not arrive. Until this is fixed, activate accounts and set passwords under Users.", "UserList", "Users");
            }
        }

        // ---- notifications ---------------------------------------------------------------------

        private async Task NotificationsAsync(HealthReport report)
        {
            const string area = "Notifications";
            try
            {
                var store = _http.HttpContext!.RequestServices.GetRequiredService<SettingsStore>();
                var hub = _http.HttpContext.RequestServices.GetRequiredService<Notify.NotificationHub>();
                var worker = _http.HttpContext.RequestServices.GetRequiredService<Notify.NotificationWorker>();
                var sms = _http.HttpContext.RequestServices.GetRequiredService<Notify.SmsSender>();

                var inApp = Notify.NotificationEvents.ChannelOn(store, Notify.NotificationEvents.InAppChannel);
                Add(report, area, "In the application", inApp ? HealthLevel.Ok : HealthLevel.Info,
                    inApp ? "Switched on. " + hub.OpenStreams + " open page(s) are listening right now." : "Switched off: nobody gets messages in the application.",
                    null, "NotificationSettings", "Notification settings");

                var smsOn = Notify.NotificationEvents.ChannelOn(store, Notify.NotificationEvents.SmsChannel);
                if (smsOn && !sms.IsConfigured)
                {
                    Add(report, area, "SMS", HealthLevel.Warning, "SMS is switched on, but no gateway is set up.",
                        "Every SMS fails until a gateway is entered.", "NotificationSettings", "Notification settings");
                }
                else if (smsOn && store.SecretIsUnreadable(Notify.SmsSender.ApiKeyKey))
                {
                    Add(report, area, "SMS", HealthLevel.Failed, "The stored gateway key can no longer be read.",
                        "The key folder App_Data/keys was replaced or lost. Enter the key again.", "NotificationSettings", "Notification settings");
                }
                else
                {
                    Add(report, area, "SMS", smsOn ? HealthLevel.Ok : HealthLevel.Info, smsOn ? "Switched on, a gateway is set up." : "Switched off.");
                }

                var now = DateTime.Now;
                var waiting = await _context.NotificationDeliveries.Where(item => item.Status == Models.Notify.NotificationDelivery.Queued).Select(item => item.CreatedAt).ToListAsync();
                var dayAgo = now.AddDays(-1);
                var failed = await _context.NotificationDeliveries.CountAsync(item => item.Status == Models.Notify.NotificationDelivery.Failed && item.CreatedAt >= dayAgo);
                var stuck = worker.LastRun == null ? (now - RecentLog.Instance.StartedAt).TotalMinutes > 2 : (now - worker.LastRun.Value).TotalMinutes > 3;
                if (stuck)
                {
                    Add(report, area, "Sender", HealthLevel.Failed, "The background sender for e-mail and SMS is not running" + (worker.LastRun == null ? "." : " (last seen " + worker.LastRun.Value.ToString("dd MMM yyyy, h:mm tt") + ")."),
                        "Queued messages stay queued. Restart the application and look at the latest warnings and errors below.");
                }
                else
                {
                    var oldest = waiting.Count == 0 ? 0 : (int)(now - waiting.Min()).TotalMinutes;
                    Add(report, area, "Sender", failed > 0 || oldest > 60 ? HealthLevel.Warning : HealthLevel.Ok,
                        waiting.Count + " message(s) wait to be sent" + (waiting.Count > 0 ? " (the oldest for " + oldest + " min)" : string.Empty) + ", " + failed + " failed in the last 24 hours.",
                        failed > 0 ? "The reason is shown next to each failed message. Fix it, then press Send failed again." : null,
                        failed > 0 || waiting.Count > 0 ? "NotificationSettings" : null, failed > 0 || waiting.Count > 0 ? "Notification settings" : null);
                }
            }
            catch (Exception exception)
            {
                Add(report, area, "Checks", HealthLevel.Warning, "The notification checks could not run: " + Short(exception));
            }
        }

        // ---- file storage ----------------------------------------------------------------------

        private async Task StorageAsync(HealthReport report, bool dbUp)
        {
            const string area = "Files";
            var folder = _tickets.UploadFolder;
            try
            {
                Directory.CreateDirectory(folder);
                var probe = Path.Combine(folder, ".health-" + Guid.NewGuid().ToString("N"));
                await File.WriteAllTextAsync(probe, "ok");
                File.Delete(probe);

                var files = new DirectoryInfo(folder).GetFiles();
                Add(report, area, "Upload folder", HealthLevel.Ok, "Attachments can be stored (" + files.Length.ToString("N0") + " files, " + Size(files.Sum(f => f.Length)) + ").");
            }
            catch (Exception exception)
            {
                Add(report, area, "Upload folder", HealthLevel.Failed, "The application cannot write to " + folder + ": " + Short(exception),
                    "Tickets can be raised, but files cannot be attached. Give the account the application runs under write access to that folder.");
            }

            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(folder)) ?? folder);
                var free = drive.AvailableFreeSpace;
                var level = free < 200L * 1024 * 1024 ? HealthLevel.Failed : free < 1024L * 1024 * 1024 ? HealthLevel.Warning : HealthLevel.Ok;
                Add(report, area, "Disk space", level, Size(free) + " free on the disk of the upload folder.",
                    level == HealthLevel.Ok ? null : "Free some space or move the application: uploads fail when the disk is full.");
            }
            catch (Exception exception)
            {
                Add(report, area, "Disk space", HealthLevel.Info, "Could not read the free disk space: " + Short(exception));
            }

            if (!dbUp)
            {
                return;
            }

            try
            {
                var rows = await _context.Attachments.OrderByDescending(a => a.AttachmentId).Take(5000).ToListAsync();
                var missing = rows.Count(row => _tickets.ResolveAttachmentPath(row) == null);
                if (missing == 0)
                {
                    Add(report, area, "Attachments", HealthLevel.Ok, "Every attachment in the database has its file" + (rows.Count == 5000 ? " (newest 5,000 checked)." : "."));
                }
                else
                {
                    Add(report, area, "Attachments", HealthLevel.Warning, missing.ToString("N0") + " of " + rows.Count.ToString("N0") + " attachments have no file on this server.",
                        "They cannot be downloaded. This happens when the database was restored without the wwwroot/Uplods folder: copy that folder from the old server.");
                }
            }
            catch (Exception exception)
            {
                Add(report, area, "Attachments", HealthLevel.Info, "Could not check the attachments: " + Short(exception));
            }
        }

        // ---- security --------------------------------------------------------------------------

        private async Task SecurityAsync(HealthReport report, bool dbUp)
        {
            const string area = "Security";
            var request = _http.HttpContext?.Request;
            var remote = _http.HttpContext?.Connection.RemoteIpAddress;
            var local = remote == null || System.Net.IPAddress.IsLoopback(remote);

            if (_environment.IsDevelopment())
            {
                Add(report, area, "Mode", local ? HealthLevel.Info : HealthLevel.Warning,
                    "The application runs in Development mode" + (local ? "." : " and is used from another computer."),
                    "Development mode shows technical details on error pages. For other people start it in Production mode (dotnet run --launch-profile lan, or ASPNETCORE_ENVIRONMENT=Production).");
            }
            else
            {
                Add(report, area, "Mode", HealthLevel.Ok, "The application runs in " + _environment.EnvironmentName + " mode.");
            }

            if (request != null && !request.IsHttps)
            {
                Add(report, area, "Encryption", local ? HealthLevel.Info : HealthLevel.Warning,
                    "This page was opened over plain http" + (local ? " on the server itself." : "."),
                    local ? "For use from other computers put the application behind https." : "Passwords cross the network unencrypted. Acceptable inside one office; brokerage houses need https (see read me.txt, section 6).");
            }
            else if (request != null)
            {
                Add(report, area, "Encryption", HealthLevel.Ok, "This page was opened over https.");
            }

            if (!dbUp)
            {
                return;
            }

            try
            {
                var seedName = _configuration["SeedAdmin:UserName"];
                var seedPassword = _configuration["SeedAdmin:Password"];
                if (!string.IsNullOrEmpty(seedName) && !string.IsNullOrEmpty(seedPassword))
                {
                    var first = await _context.Users.FirstOrDefaultAsync(u => u.UserName == seedName);
                    if (first != null && PasswordHasher.Verify(seedPassword, first.PasswordHash, first.PasswordSalt))
                    {
                        Add(report, area, "First administrator", HealthLevel.Warning, "The account “" + seedName + "” still has the password that is written in appsettings.Development.json.",
                            "Everybody who can read that file can sign in as administrator. Change it now.", "ChangePassword", "Change password");
                    }
                    else
                    {
                        Add(report, area, "First administrator", HealthLevel.Ok, "The password from the settings file is no longer in use.");
                    }
                }

                var admins = await _context.Users.CountAsync(u => u.UCatagory && u.UStatus && u.VerifiedAt != null);
                Add(report, area, "Platform admins", admins == 1 ? HealthLevel.Info : HealthLevel.Ok,
                    admins + (admins == 1 ? " active platform admin." : " active platform admins."),
                    admins == 1 ? "If this one account is lost, nobody can administer the system. Create a second platform admin." : null,
                    admins == 1 ? "CreateUser" : null, admins == 1 ? "New user" : null);

                var since = DateTime.Now.AddHours(-24);
                var failed = await _context.AuditLogs.CountAsync(line => line.Action == AuditActions.SignInFailed && line.At >= since);
                Add(report, area, "Failed sign-ins", failed >= 20 ? HealthLevel.Warning : HealthLevel.Ok,
                    failed + " failed sign-in(s) with existing accounts in the last 24 hours.",
                    failed >= 20 ? "Many wrong passwords can mean somebody is guessing. Look at who and from where." : null,
                    failed >= 20 ? "AuditTrail" : null, failed >= 20 ? "Audit trail" : null);
            }
            catch (Exception exception)
            {
                Add(report, area, "Accounts", HealthLevel.Info, "Could not check the administrator accounts: " + Short(exception));
            }
        }

        // ---- is the support work stuck? --------------------------------------------------------

        private async Task OperationsAsync(HealthReport report)
        {
            const string area = "Support work";
            try
            {
                var waiting = await _context.Users.CountAsync(u => u.VerifiedAt == null && u.UStatus);
                Add(report, area, "Accounts waiting", waiting > 0 ? HealthLevel.Warning : HealthLevel.Ok,
                    waiting == 0 ? "No registered account waits for activation." : waiting + " registered account(s) wait for activation.",
                    waiting > 0 ? "They registered but never entered the token from the e-mail. Activate the ones you know." : null,
                    waiting > 0 ? "UserList" : null, waiting > 0 ? "Users" : null);

                var houses = await _context.Brokerages.Select(b => new { b.BrokerageId, b.BrokerageHouseName }).ToListAsync();
                var withBranch = (await _context.Branchhs.Select(b => b.BrokerageId).Distinct().ToListAsync()).ToHashSet();
                var noBranch = houses.Where(h => !withBranch.Contains(h.BrokerageId)).Select(h => h.BrokerageHouseName).ToList();
                Add(report, area, "Brokerage houses", noBranch.Count > 0 ? HealthLevel.Warning : HealthLevel.Ok,
                    noBranch.Count == 0 ? houses.Count + " brokerage house(s), each with at least one branch." : "No branch yet: " + string.Join(", ", noBranch.Take(8)) + (noBranch.Count > 8 ? ", …" : "") + ".",
                    noBranch.Count > 0 ? "Nobody can register for a house without a branch." : null,
                    noBranch.Count > 0 ? "BrocarageHouseList" : null, noBranch.Count > 0 ? "Brokerage houses" : null);

                var housesWithAdmin = (await _context.Users.Where(u => !u.UType && !u.UCatagory && u.Department == Rbac.HouseAdminPosition && u.UStatus)
                    .Select(u => u.BrokerageHouseName).Distinct().ToListAsync()).ToHashSet();
                var housesWithUsers = (await _context.Users.Where(u => !u.UType && !u.UCatagory).Select(u => u.BrokerageHouseName).Distinct().ToListAsync()).ToHashSet();
                var noAdmin = houses.Count(h => housesWithUsers.Contains(h.BrokerageId) && !housesWithAdmin.Contains(h.BrokerageId));
                Add(report, area, "House admins", noAdmin > 0 ? HealthLevel.Info : HealthLevel.Ok,
                    noAdmin == 0 ? "Every brokerage house with users has a house admin." : noAdmin + " brokerage house(s) with users have no house admin.",
                    noAdmin > 0 ? "Their accounts are managed by you alone. Give one person of each house the role House admin (Users > Edit)." : null,
                    noAdmin > 0 ? "UserList" : null, noAdmin > 0 ? "Users" : null);

                var engineers = await _tickets.Engineers().Select(u => new { u.Id, u.FullName }).ToListAsync();
                Add(report, area, "Support engineers", engineers.Count == 0 ? HealthLevel.Failed : HealthLevel.Ok,
                    engineers.Count == 0 ? "There is no active support engineer." : engineers.Count + " active support engineer(s).",
                    engineers.Count == 0 ? "Tickets cannot be assigned to anybody. Create a user with the role Support engineer." : null,
                    engineers.Count == 0 ? "CreateUser" : null, engineers.Count == 0 ? "New user" : null);

                var open = await _context.Issues.Where(i => i.IStatus != TicketStatus.Closed).Select(i => new { i.AssignedToId, i.AssignBy, i.TDate }).ToListAsync();
                var unassigned = open.Where(t => t.AssignedToId == null && string.IsNullOrEmpty(t.AssignBy)).ToList();
                var oldest = unassigned.Count == 0 ? 0 : (int)(DateTime.Now - unassigned.Min(t => t.TDate)).TotalDays;
                Add(report, area, "Unassigned tickets", oldest >= 3 ? HealthLevel.Warning : HealthLevel.Ok,
                    unassigned.Count == 0 ? "Every open ticket has an engineer (" + open.Count + " open)." : unassigned.Count + " of " + open.Count + " open tickets have no engineer; the oldest has waited " + oldest + " day(s).",
                    oldest >= 3 ? "Tickets should not wait for days. Assign them." : null,
                    unassigned.Count > 0 ? "Workload" : null, unassigned.Count > 0 ? "Workload" : null);

                var ids = engineers.Select(e => e.Id).ToHashSet();
                var names = engineers.Select(e => e.FullName).ToHashSet();
                var orphaned = open.Count(t => (t.AssignedToId != null && !ids.Contains(t.AssignedToId.Value)) || (t.AssignedToId == null && !string.IsNullOrEmpty(t.AssignBy) && !names.Contains(t.AssignBy)));
                if (orphaned > 0)
                {
                    Add(report, area, "Tickets without a working engineer", HealthLevel.Warning,
                        orphaned + " open ticket(s) are assigned to somebody who is no longer an active support engineer.",
                        "Nobody finds them under “Assigned to me”. Reassign them.", "Workload", "Workload");
                }
            }
            catch (Exception exception)
            {
                Add(report, area, "Checks", HealthLevel.Warning, "The support checks could not run: " + Short(exception));
            }
        }

        // ---- facts -----------------------------------------------------------------------------

        private void Facts(HealthReport report)
        {
            var process = Process.GetCurrentProcess();
            var started = RecentLog.Instance.StartedAt;
            var up = DateTime.Now - started;
            string addresses;
            try
            {
                addresses = string.Join(", ", _server.Features.Get<IServerAddressesFeature>()?.Addresses ?? Array.Empty<string>());
            }
            catch
            {
                addresses = string.Empty;
            }

            report.Facts.Add(("Application", "Xpert CSMS " + Ui.Version));
            report.Facts.Add(("Mode", _environment.EnvironmentName));
            report.Facts.Add(("Started", started.ToString("dd MMM yyyy, h:mm tt") + " (" + (up.TotalDays >= 1 ? (int)up.TotalDays + " d " : "") + up.Hours + " h " + up.Minutes + " min ago)"));
            report.Facts.Add(("Server time", DateTime.Now.ToString("dd MMM yyyy, h:mm:ss tt") + ", " + TimeZoneInfo.Local.DisplayName));
            report.Facts.Add(("Listens on", string.IsNullOrEmpty(addresses) ? "(behind a web server)" : addresses));
            report.Facts.Add(("Runtime", RuntimeInformation.FrameworkDescription));
            report.Facts.Add(("Operating system", RuntimeInformation.OSDescription));
            report.Facts.Add(("Computer", Environment.MachineName + ", " + Environment.ProcessorCount + " processors"));
            report.Facts.Add(("Memory in use", Size(process.WorkingSet64)));
            string database;
            try
            {
                var connection = _context.Database.GetDbConnection();
                database = connection.Database + " on " + connection.DataSource;
            }
            catch
            {
                database = "(unknown)";
            }

            report.Facts.Add(("Database", database));
            report.Facts.Add(("Upload folder", _tickets.UploadFolder));
        }

        private static string Short(Exception exception)
        {
            var message = (exception.InnerException ?? exception).Message.Replace("\r", " ").Replace("\n", " ");
            return message.Length > 220 ? message.Substring(0, 220) + "…" : message;
        }

        private static string Size(long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024) { return (bytes / (1024d * 1024 * 1024)).ToString("0.0") + " GB"; }
            if (bytes >= 1024 * 1024) { return (bytes / (1024d * 1024)).ToString("0.0") + " MB"; }
            return (bytes / 1024d).ToString("0") + " KB";
        }
    }
}
