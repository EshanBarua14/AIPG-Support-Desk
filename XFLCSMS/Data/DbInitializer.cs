using XFLCSMS.Models.Branch;
using XFLCSMS.Models.Brocarage;
using XFLCSMS.Models.Register;
using XFLCSMS.Services;

namespace XFLCSMS.Data
{
    /// <summary>
    /// Runs once at start-up:
    ///  1. applies pending EF Core migrations (creates the database on a fresh SQL Server);
    ///  2. on a database without any user, creates the first administrator from the "SeedAdmin" settings.
    ///     Without it a fresh database cannot be used at all: registering needs a brokerage house and a
    ///     branch, and only an administrator can create those.
    /// </summary>
    public static class DbInitializer
    {
        public static void Initialize(IServiceProvider services, IConfiguration configuration, ILogger logger)
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DataContext>();

            try
            {
                if (configuration.GetValue("Database:AutoMigrate", true))
                {
                    db.Database.Migrate();
                }

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
                        BrokerageHouseName = seed["BrokerageHouseName"] ?? "Xpert Fintech Limited",
                        BrokerageHouseAcronym = seed["BrokerageHouseAcronym"] ?? "XFL"
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
                    Terms = true
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
