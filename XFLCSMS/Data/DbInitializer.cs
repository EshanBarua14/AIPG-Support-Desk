using XFLCSMS.Models.Branch;
using XFLCSMS.Models.Brocarage;
using XFLCSMS.Infrastructure;
using XFLCSMS.Models.Register;
using XFLCSMS.Models.Support;
using XFLCSMS.Services;

namespace XFLCSMS.Data
{
    /// <summary>
    /// Runs once at start-up:
    ///  1. applies pending EF Core migrations (creates the database on a fresh SQL Server);
    ///  2. once, on a database without any product, adds the starting products with their support types and
    ///     categories (Data/ProductSeed.cs);
    ///  3. on a database without any user, creates the first administrator from the "SeedAdmin" settings.
    ///     Without it a fresh database cannot be used at all: registering needs a brokerage house and a
    ///     branch, and only an administrator can create those.
    /// </summary>
    public static class DbInitializer
    {
        /// <summary>
        /// Brings tickets written by older versions in line with the statuses of Services/TicketStatus.cs:
        /// an open ticket without an engineer is "Unassigned" (stored "Open"), an open ticket with an engineer has
        /// at least "Assigned" (stored "Inqueue"), other spellings ("Closed", "In Progress") get the stored one.
        /// Does nothing once the tickets are in line.
        /// </summary>
        private static void NormaliseStatuses(DataContext db, ILogger logger)
        {
            var known = TicketStatus.Keys;
            var work = TicketStatus.Work;
            // Spellings are compared here, letter for letter: the database treats "close" and "Close" as equal, the
            // application does not. There are only a handful of different values, whatever the number of tickets.
            var spellings = db.Issues.Select(i => i.IStatus).Distinct().ToList();
            var wrong = spellings.Where(value => value != null && !known.Contains(value, StringComparer.Ordinal)).Select(value => value!).ToList();
            var odd = db.Issues.Where(i =>
                    i.IStatus == null || wrong.Contains(i.IStatus)
                    || (i.IStatus == TicketStatus.Unassigned && (i.AssignedToId != null || (i.AssignBy != null && i.AssignBy != "")))
                    || (work.Contains(i.IStatus) && i.AssignedToId == null && (i.AssignBy == null || i.AssignBy == "")))
                .ToList();
            // (a query that matched "Close" for the wrong spelling "close" also returns the rows that are right)
            odd = odd.Where(i =>
                {
                    if (i.IStatus == null || !known.Contains(i.IStatus, StringComparer.Ordinal)) { return true; }   // the spelling
                    if (i.IStatus == TicketStatus.Closed) { return false; }
                    return (i.IStatus == TicketStatus.Unassigned) != TicketService.IsUnassigned(i);                 // status against assignment
                })
                .ToList();
            if (odd.Count == 0)
            {
                return;
            }

            foreach (var issue in odd)
            {
                var free = TicketService.IsUnassigned(issue);
                var status = TicketStatus.Normalize(issue.IStatus);
                if (status == TicketStatus.Closed)
                {
                    issue.IStatus = TicketStatus.Closed;
                }
                else if (free)
                {
                    issue.IStatus = TicketStatus.Unassigned;
                }
                else
                {
                    issue.IStatus = status == null || status == TicketStatus.Unassigned ? TicketStatus.Assigned : status;
                }
            }

            db.AuditLogs.Add(new XFLCSMS.Models.Audit.AuditLog
            {
                At = DateTime.Now, UserName = "System", Action = AuditActions.SystemStatusFix, EntityType = "System", EntityLabel = "Ticket statuses",
                Details = "Brought the status of " + odd.Count + (odd.Count == 1 ? " ticket" : " tickets") + " in line with its assignment (unassigned tickets are \u201cUnassigned\u201d, assigned ones at least \u201cAssigned\u201d)"
            });
            db.SaveChanges();
            logger.LogWarning("Brought the status of {Count} tickets in line with their assignment.", odd.Count);
        }

        /// <summary>
        /// Version 3.3 replaced the one "Comments" field of a ticket by a conversation. What stood in that field becomes
        /// the first entry of the ticket's conversation, once. Who wrote it was never recorded, so the entry says so.
        /// The old column keeps its text; nothing is lost.
        /// </summary>
        private static void MoveCommentsIntoConversations(DataContext db, SettingsStore settings, ILogger logger)
        {
            const string done = "desk.comments_moved";
            if (settings.GetBool(done, false))
            {
                return;
            }

            var withComments = db.Issues.Where(issue => issue.Comments != null && issue.Comments != "")
                .Select(issue => new { issue.IssueId, issue.Comments, issue.TDate, issue.UpdatedOn }).ToList()
                .Where(issue => !string.IsNullOrWhiteSpace(issue.Comments)).ToList();
            var already = db.TicketMessages.Select(message => message.IssueId).Distinct().ToList().ToHashSet();
            var moved = 0;
            foreach (var issue in withComments.Where(issue => !already.Contains(issue.IssueId)))
            {
                db.TicketMessages.Add(new XFLCSMS.Models.Desk.TicketMessage
                {
                    IssueId = issue.IssueId,
                    AuthorName = "Comments field",
                    AuthorRole = "before version 3.3",
                    At = issue.UpdatedOn ?? issue.TDate,
                    Body = System.Net.WebUtility.HtmlEncode(issue.Comments!.Trim()).Replace("\r\n", "<br>").Replace("\n", "<br>")
                });
                moved++;
            }

            settings.Stage(db, new Dictionary<string, string?> { [done] = "true" }, "System");
            db.SaveChanges();
            settings.Reload();
            if (moved > 0)
            {
                logger.LogWarning("Moved the comments of {Count} tickets into their conversations.", moved);
            }
        }

        /// <summary>A few canned replies to start with, added once. They are ordinary entries: change or remove them.</summary>
        private static void AddStarterCannedReplies(DataContext db, SettingsStore settings)
        {
            const string done = "desk.canned_added";
            if (settings.GetBool(done, false))
            {
                return;
            }

            if (!db.CannedReplies.Any())
            {
                var now = DateTime.Now;
                foreach (var (title, body) in new[]
                {
                    ("We have your ticket", "<p>Dear {name},</p><p>thank you for ticket {ticket}. We are looking into it and will write here as soon as we know more.</p><p>{me}<br>AIPG Support</p>"),
                    ("Ask for a screenshot", "<p>Dear {name},</p><p>to find the cause we need a little more from you:</p><ul><li>a screenshot of the screen with the message,</li><li>the time it happened,</li><li>the user who was signed in.</li></ul><p>You can attach files to your reply here.</p><p>{me}</p>"),
                    ("Fix is deployed", "<p>Dear {name},</p><p>the fix for ticket {ticket} is deployed. Please try again and tell us here whether it works for you.</p><p>{me}<br>AIPG Support</p>"),
                    ("Closing the ticket", "<p>Dear {name},</p><p>we have not heard of further trouble, so we are closing ticket {ticket}. If the problem comes back, write here or raise a new ticket.</p><p>{me}</p>")
                })
                {
                    db.CannedReplies.Add(new XFLCSMS.Models.Desk.CannedReply { Title = title, Body = body, IsActive = true, UpdatedAt = now, UpdatedBy = "System" });
                }
            }

            settings.Stage(db, new Dictionary<string, string?> { [done] = "true" }, "System");
            db.SaveChanges();
            settings.Reload();
        }

        /// <summary>
        /// Adds the starting products with their support types and categories (Data/ProductSeed.cs) - once: a
        /// database that has a product, or that was filled before, is left alone, so what an administrator renamed
        /// or deleted does not come back at the next start.
        /// </summary>
        private static void SeedProducts(DataContext db, SettingsStore settings, IConfiguration configuration, ILogger logger)
        {
            // asked of the database itself, not of the settings in memory: those keep their old (empty) values when
            // the table could not be read, and the products an administrator deleted would come back
            if (!configuration.GetValue("Products:Seed", true) || db.AppSettings.Any(setting => setting.Name == SettingsStore.ProductsSeeded))
            {
                return;
            }

            if (!db.Products.Any())
            {
                foreach (var entry in ProductSeed.Products)
                {
                    var product = new Product { Name = entry.Name, Code = entry.Code, Description = entry.Description, IsActive = true };
                    db.Products.Add(product);
                    foreach (var name in entry.Types)
                    {
                        db.SupportTypes.Add(new SupportType { SType = name, Product = product });
                    }

                    foreach (var name in entry.Categories)
                    {
                        db.SupportCatagories.Add(new SupportCatagory { SCatagory = name, Product = product });
                    }
                }

                db.AuditLogs.Add(new XFLCSMS.Models.Audit.AuditLog
                {
                    At = DateTime.Now, UserName = "System", Action = AuditActions.DataCreate, EntityType = "Product", EntityLabel = "Starting products",
                    Details = "Added the starting products (" + string.Join(", ", ProductSeed.Products.Select(entry => entry.Name)) + ") with their support types and categories"
                });
                logger.LogWarning("Added {Count} starting products with their support types and categories. Change them under Administration > Products.", ProductSeed.Products.Length);
            }

            settings.Stage(db, new Dictionary<string, string?> { [SettingsStore.ProductsSeeded] = "1" }, "System");
            db.SaveChanges();
            settings.Reload();
        }

        public static void Initialize(IServiceProvider services, IConfiguration configuration, ILogger logger)
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DataContext>();

            // What each role may do, as saved on the permissions page (nothing saved: the defaults). Subscribed before
            // anything can fail, so the table is also picked up when the database only becomes reachable later.
            var settings = services.GetRequiredService<SettingsStore>();
            settings.Changed += () => Rbac.Apply(Rbac.Parse(settings.Get(SettingsStore.RbacGrants)));

            try
            {
                if (configuration.GetValue("Database:AutoMigrate", true))
                {
                    // When the computer starts, the site can be up a few seconds before SQL Server is. The updates
                    // of the database are applied here and nowhere else, so it is worth waiting a moment for it.
                    for (var attempt = 1; ; attempt++)
                    {
                        try
                        {
                            db.Database.Migrate();
                            break;
                        }
                        catch (Exception exception) when (attempt < 5)
                        {
                            logger.LogWarning("The database is not reachable yet ({Reason}). Trying again in 3 seconds ({Attempt} of 5).", exception.GetBaseException().Message, attempt);
                            Thread.Sleep(TimeSpan.FromSeconds(3));
                        }
                    }
                }

                settings.Reload();

                NormaliseStatuses(db, logger);

                SeedProducts(db, settings, configuration, logger);
                MoveCommentsIntoConversations(db, settings, logger);
                AddStarterCannedReplies(db, settings);

                if (db.Users.Any())
                {
                    return;
                }

                var seed = configuration.GetSection("SeedAdmin");
                var userName = seed["UserName"];
                var password = seed["Password"];
                if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
                {
                    logger.LogWarning("The database has no users and no SeedAdmin:UserName / SeedAdmin:Password is configured, so nobody can sign in yet.");
                    return;
                }

                var house = db.Brokerages.OrderBy(b => b.BrokerageId).FirstOrDefault();
                if (house == null)
                {
                    house = new Brokerage
                    {
                        BrokerageHouseName = seed["BrokerageHouseName"] ?? Ui.SupportTeam,
                        BrokerageHouseAcronym = seed["BrokerageHouseAcronym"] ?? "AIPG"
                    };
                    db.Brokerages.Add(house);
                    db.SaveChanges();
                }

                var branch = db.Branchhs.OrderBy(b => b.BranchId).FirstOrDefault(b => b.BrokerageId == house.BrokerageId);
                if (branch == null)
                {
                    branch = new Branchh { BranchName = seed["BranchName"] ?? "Head Office", BrokerageId = house.BrokerageId };
                    db.Branchhs.Add(branch);
                    db.SaveChanges();
                }

                PasswordHasher.Create(password, out byte[] passwordHash, out byte[] passwordSalt);
                db.Users.Add(new User
                {
                    FullName = seed["FullName"] ?? "System Administrator",
                    Email = seed["Email"] ?? "admin@example.com",
                    PhonNumber = seed["PhonNumber"] ?? "0",
                    Designation = "Administrator",
                    Department = "Maker",
                    EmployeeId = "0001",
                    UserName = userName,
                    PasswordHash = passwordHash,
                    PasswordSalt = passwordSalt,
                    VerifiedAt = DateTime.Now,
                    BrokerageHouseName = house.BrokerageId,
                    BrokerageHouseAcronym = house.BrokerageId,
                    Branch = branch.BranchId,
                    UCatagory = true,
                    UType = true,
                    UStatus = true,
                    Terms = true,
                    MustChangePassword = true // this password stands in a settings file: good for the first sign-in only
                });
                db.SaveChanges();

                logger.LogWarning("Empty database: created the first administrator '{UserName}'. Sign in and change the password.", userName);
            }
            catch (Exception ex)
            {
                // Keep the site up so the cause is visible in the log instead of a start-up crash.
                logger.LogError(ex, "Could not initialise the database. Check ConnectionStrings:DefaultConnection in appsettings.json and that SQL Server is running.");
            }
        }
    }
}
