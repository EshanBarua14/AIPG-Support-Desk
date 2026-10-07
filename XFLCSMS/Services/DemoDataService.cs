using System.Text.Json;
using XFLCSMS.Infrastructure;
using XFLCSMS.Models.Affected;
using XFLCSMS.Models.Audit;
using XFLCSMS.Models.Branch;
using XFLCSMS.Models.Brocarage;
using XFLCSMS.Models.Issue;
using XFLCSMS.Models.Notify;
using XFLCSMS.Models.Register;
using XFLCSMS.Models.Support;
using XFLCSMS.Models.Todos;
using XFLCSMS.Services.Notify;

namespace XFLCSMS.Services
{
    public class DemoAccount
    {
        public string Role { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string House { get; set; } = string.Empty;
    }

    public class DemoStatus
    {
        public bool Loaded { get; set; }
        public DateTime? LoadedOn { get; set; }
        public string? LoadedBy { get; set; }
        public int Houses { get; set; }
        public int Users { get; set; }
        public int Tickets { get; set; }
        public int Todos { get; set; }
        public List<DemoAccount> Accounts { get; set; } = new();
        /// <summary>The password of the demo accounts of this load; null when it can no longer be read.</summary>
        public string? Password { get; set; }
        /// <summary>Tickets the demo accounts raised after the load: they go when the demo data is removed.</summary>
        public int ExtraTickets { get; set; }
        /// <summary>Tickets and accounts that are not demo data.</summary>
        public int RealTickets { get; set; }
        public int RealUsers { get; set; }
    }

    public class DemoResult
    {
        public bool Ok { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// Demo data: three brokerage houses with branches, people of every role, about sixty tickets in every status
    /// with their history, to-dos and notifications - enough to see every page of the application with content.
    /// Loaded and removed from the page System > Demo data. Everything that is created is written down (setting
    /// "demo.ids"), so removing it takes away exactly that and nothing else. The demo accounts have addresses under
    /// "demo.invalid": no e-mail or SMS is ever sent to them.
    ///
    /// The demo accounts are real accounts: the demo manager and engineers are XFL staff and see every ticket. So each
    /// load gets its own random password (shown on the page to whoever may load demo data), and loading next to
    /// real tickets needs an explicit yes.
    /// </summary>
    public class DemoDataService
    {
        private const string MailDomain = "@demo.invalid";
        private const string NamePrefix = "demo.";

        // one load or removal at a time (the application is one process)
        private static readonly SemaphoreSlim Gate = new(1, 1);

        /// <summary>A password for one load: "Demo@" and six random letters and digits (fits the password rule of the system).</summary>
        private static string NewPassword()
        {
            const string letters = "abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ";
            const string digits = "23456789";
            var all = letters + digits;
            var chars = new char[6];
            for (var i = 0; i < chars.Length; i++)
            {
                chars[i] = all[System.Security.Cryptography.RandomNumberGenerator.GetInt32(all.Length)];
            }

            // at least one digit, wherever the dice put the letters
            chars[System.Security.Cryptography.RandomNumberGenerator.GetInt32(chars.Length)] = digits[System.Security.Cryptography.RandomNumberGenerator.GetInt32(digits.Length)];
            return "Demo@" + new string(chars);
        }

        private readonly DataContext _db;
        private readonly SettingsStore _settings;
        private readonly AuditService _audit;

        public DemoDataService(DataContext db, SettingsStore settings, AuditService audit)
        {
            _db = db;
            _settings = settings;
            _audit = audit;
        }

        /// <summary>What was created, so it can be removed again.</summary>
        private class Ledger
        {
            public DateTime On { get; set; }
            public string? By { get; set; }
            /// <summary>The password of this load, encrypted like the other stored secrets.</summary>
            public string? Secret { get; set; }
            public List<int> Houses { get; set; } = new();
            public List<int> Branches { get; set; } = new();
            public List<int> Users { get; set; } = new();
            public List<int> Issues { get; set; } = new();
            public List<int> Todos { get; set; } = new();
            public List<int> SupportTypes { get; set; } = new();
            public List<int> Categories { get; set; } = new();
            public List<int> SubCategories { get; set; } = new();
            public List<int> Sections { get; set; } = new();
        }

        private Ledger? ReadLedger()
        {
            var text = _settings.Get(SettingsStore.DemoData);
            if (text == null) { return null; }
            try { return JsonSerializer.Deserialize<Ledger>(text); }
            catch (JsonException) { return null; }
        }

        public async Task<DemoStatus> StatusAsync()
        {
            var ledger = ReadLedger();
            var status = new DemoStatus { Loaded = ledger != null, LoadedOn = ledger?.On, LoadedBy = ledger?.By };
            if (ledger?.Secret != null)
            {
                status.Password = _settings.Reveal(ledger.Secret);
            }

            var demoUsers = ledger?.Users ?? new List<int>();
            var demoIssues = ledger?.Issues ?? new List<int>();
            status.RealUsers = await _db.Users.CountAsync(u => !demoUsers.Contains(u.Id));
            status.RealTickets = await _db.Issues.CountAsync(i => !demoIssues.Contains(i.IssueId));
            if (ledger == null) { return status; }

            var houses = await _db.Brokerages.ToDictionaryAsync(b => b.BrokerageId, b => b.BrokerageHouseName);
            var users = (await _db.Users.Where(u => ledger.Users.Contains(u.Id)).ToListAsync()).Where(IsDemoAccount).ToList();
            var userIds = users.Select(u => u.Id).ToList();
            status.ExtraTickets = await _db.Issues.CountAsync(i => userIds.Contains(i.UserId) && !ledger.Issues.Contains(i.IssueId));
            status.Houses = await _db.Brokerages.CountAsync(b => ledger.Houses.Contains(b.BrokerageId));
            status.Users = users.Count;
            status.Tickets = await _db.Issues.CountAsync(i => ledger.Issues.Contains(i.IssueId));
            status.Todos = await _db.Todos.CountAsync(t => ledger.Todos.Contains(t.Id));
            status.Accounts = users
                .OrderBy(u => Array.IndexOf(Rbac.AllRoles, Rbac.RoleOf(u))).ThenBy(u => u.BrokerageHouseName).ThenBy(u => u.UserName)
                .Select(u => new DemoAccount
                {
                    Role = Rbac.Label(Rbac.RoleOf(u)),
                    FullName = u.FullName,
                    UserName = u.UserName,
                    House = Rbac.IsStaff(Rbac.RoleOf(u)) ? "XFL" : houses.GetValueOrDefault(u.BrokerageHouseName, string.Empty)
                })
                .ToList();
            return status;
        }

        // ---- the content ---------------------------------------------------------------------------

        private static readonly (string Name, string Acronym, string[] Branches)[] Houses =
        {
            ("Demo Alpha Securities Ltd.", "DEMOA", new[] { "Motijheel Head Office", "Gulshan Branch", "Chattogram Branch" }),
            ("Demo Beta Capital Ltd.", "DEMOB", new[] { "Dilkusha Head Office", "Dhanmondi Branch" }),
            ("Demo Gamma Brokerage Ltd.", "DEMOC", new[] { "Banani Head Office", "Sylhet Branch" })
        };

        private static readonly string[] SupportTypes = { "Incident", "Request", "Change", "Question" };
        private static readonly string[] Categories = { "Trading", "Settlement", "BackOffice", "Reports", "Access" };
        private static readonly string[] SubCategories = { "OrderEntry", "Confirmation", "Ledger", "Statement", "Password" };
        private static readonly string[] Sections = { "Front office", "Back office", "Risk management", "Accounts" };

        // (user name, full name, designation)
        private static readonly (string UserName, string FullName, string Designation, Role Role)[] Staff =
        {
            ("demo.manager", "Shahana Rahman", "Support Manager", Role.SupportManager),
            ("demo.engineer1", "Tanvir Hasan", "Senior Support Engineer", Role.SupportEngineer),
            ("demo.engineer2", "Nusrat Jahan", "Support Engineer", Role.SupportEngineer),
            ("demo.engineer3", "Arif Chowdhury", "Support Engineer", Role.SupportEngineer)
        };

        private static readonly string[][] HousePeople =
        {
            new[] { "Farhana Akter", "Imran Hossain", "Sadia Islam", "Rakib Uddin" },
            new[] { "Mahmud Kabir", "Tasnim Sultana", "Jubayer Alam" },
            new[] { "Rumana Haque", "Sabbir Ahmed", "Lamia Karim" }
        };

        // (title, details, category index, type index, section index)
        private static readonly (string Title, string Details, int Category, int Type, int Section)[] Problems =
        {
            ("Buy orders rejected with error 1042 since market open", "Every buy order from the dealer terminals comes back with error 1042 (limit exceeded). The client limits were not changed. Sell orders go through.", 0, 0, 0),
            ("Trade confirmation e-mails are sent twice", "Since yesterday clients receive each trade confirmation two times, a few seconds apart. The contract notes themselves are correct.", 1, 0, 1),
            ("Client ledger balance differs from the statement", "For client code 14530 the ledger shows a closing balance that is 2,350.00 higher than the monthly statement. It looks like one charge is missing from the ledger.", 2, 0, 3),
            ("Add a column for average cost to the portfolio report", "Our relationship managers ask for the average cost per share in the portfolio report, next to the market price.", 3, 2, 1),
            ("New dealer cannot sign in to the trading terminal", "A dealer who joined this week gets the message that the account is locked at the first sign-in. The account was created on Monday.", 4, 1, 0),
            ("Settlement file for T+2 was not generated", "The end-of-day job finished without the settlement file for today's T+2 trades. The log shows no error. We need the file before 10:00.", 1, 0, 1),
            ("Order book freezes for a few seconds every hour", "At about ten past every hour the order book stops updating for 5 to 8 seconds on all terminals, then catches up.", 0, 0, 0),
            ("How do we change the commission rate for one client?", "We agreed a lower commission with an institutional client. Where is the rate set per client, and from when does it apply?", 2, 3, 3),
            ("Daily trade summary shows yesterday's date in the header", "The PDF of the daily trade summary carries yesterday's date in the header; the trades listed are the right ones.", 3, 0, 1),
            ("Margin call report lists closed accounts", "Three accounts that were closed last month still appear in the margin call report with a zero balance.", 3, 0, 2),
            ("Request for a second approver on fund withdrawals", "Compliance asks that withdrawals above 500,000 need a second approval before they are released.", 2, 2, 3),
            ("Password reset link expires too quickly", "Several investors complained that the reset link no longer works when they open the e-mail after about ten minutes.", 4, 0, 0),
            ("Bulk upload of new client accounts fails on row 213", "The CSV upload stops at row 213 with the message 'invalid BO id'. The BO id on that row is 16 digits like the others.", 2, 0, 1),
            ("Contract note shows the wrong settlement date for block trades", "Block trades done on Thursday show Sunday as settlement date; it should be Monday.", 1, 0, 1),
            ("Need read-only access for our external auditors", "Two auditors need to see ledgers and statements for the last financial year, without being able to change anything.", 4, 1, 3),
            ("Price feed shows a stale price for one instrument", "The last traded price of one instrument has not changed since 11:20 although trades are reported by the exchange.", 0, 0, 0),
            ("Month-end interest posting ran twice for margin accounts", "The interest for margin accounts was posted twice for this month. Please reverse the second posting.", 2, 0, 3),
            ("Export of the client list to Excel times out", "With more than 20,000 clients the export runs for a minute and then shows a time-out page.", 3, 0, 1),
            ("Add the branch name to the dealer-wise turnover report", "Head office needs the turnover per dealer grouped by branch.", 3, 2, 1),
            ("Investor portal shows portfolio value as zero on mobile", "On phones the portfolio page shows 0.00 as total value; the same account is correct on a desktop browser.", 0, 0, 0),
            ("IPO application file is rejected by the exchange", "The file we generate for the current IPO is rejected with 'record length mismatch' on upload to the exchange.", 1, 0, 1),
            ("Can dormant accounts be hidden from the dealer screen?", "Dealers scroll through many dormant accounts. Is there a setting to hide accounts without trades for twelve months?", 0, 3, 0),
            ("Cheque deposit entries are missing the bank name", "Since the last update the deposit entry screen no longer saves the bank name; the field is empty in the report.", 2, 0, 3),
            ("Risk limit change needs to take effect without a restart", "Changing a client's exposure limit only works after the trading service is restarted. We need it to apply at once.", 0, 2, 2)
        };

        private static readonly string[] EngineerNotes =
        {
            "Reproduced on the test system. Looking at the log of the order gateway.",
            "Waiting for the house to send the file that failed.",
            "Fix prepared; a colleague is checking it before it goes to production.",
            "Cause found: a setting was lost in the last update. Corrected.",
            "Asked the exchange for the reject codes of that day.",
            "The change is in the build of this week."
        };

        private static readonly string[] TodoTexts =
        {
            "Call the branch manager about the open settlement ticket",
            "Check the end-of-day log after the fix",
            "Prepare the list of dealers for the access review",
            "Send the monthly ticket summary to head office",
            "Read the release notes of the trading update",
            "Follow up on the auditors' read-only access",
            "Collect screenshots for the margin report issue"
        };

        // how many demo tickets get each status
        private static readonly (string Status, int Count)[] Spread =
        {
            (TicketStatus.Unassigned, 9), (TicketStatus.Assigned, 8), (TicketStatus.InProgress, 10), (TicketStatus.Pending, 6),
            (TicketStatus.Review, 6), (TicketStatus.Done, 6), (TicketStatus.Deployed, 5), (TicketStatus.Closed, 14)
        };

        /// <summary>Still one of ours? An account that was renamed or given a real address is no longer demo data.</summary>
        private static bool IsDemoAccount(User user)
        {
            return user.UserName.StartsWith(NamePrefix, StringComparison.OrdinalIgnoreCase) && user.Email.EndsWith(MailDomain, StringComparison.OrdinalIgnoreCase);
        }

        // ---- load ----------------------------------------------------------------------------------

        /// <param name="besideRealData">The administrator has confirmed that demo staff accounts may exist next to real tickets.</param>
        public async Task<DemoResult> LoadAsync(User actor, bool besideRealData)
        {
            await Gate.WaitAsync();
            try
            {
                _settings.Reload(); // somebody else may have loaded it a moment ago
                return await LoadLockedAsync(actor, besideRealData);
            }
            finally
            {
                Gate.Release();
            }
        }

        private async Task<DemoResult> LoadLockedAsync(User actor, bool besideRealData)
        {
            if (ReadLedger() != null)
            {
                return new DemoResult { Message = "The demo data is already loaded." };
            }

            if (!besideRealData && await _db.Issues.AnyAsync())
            {
                return new DemoResult { Message = "This system holds tickets. Tick the box to confirm that demo accounts may be created next to them." };
            }

            var acronyms = Houses.Select(house => house.Acronym).ToList();
            var names = Houses.Select(house => house.Name).ToList();
            if (await _db.Brokerages.AnyAsync(b => acronyms.Contains(b.BrokerageHouseAcronym) || names.Contains(b.BrokerageHouseName)))
            {
                return new DemoResult { Message = "A brokerage house with a demo name or acronym (DEMOA, DEMOB, DEMOC) already exists. Rename or delete it first." };
            }

            if (await _db.Users.AnyAsync(u => u.UserName.StartsWith(NamePrefix) || u.Email.EndsWith(MailDomain)))
            {
                return new DemoResult { Message = "There are already accounts whose user name starts with “demo.”. Delete them first." };
            }

            var staffHouse = await _db.Users.Where(u => u.Id == actor.Id).Select(u => new { u.BrokerageHouseName, u.Branch }).FirstAsync();
            var password = NewPassword();
            var ledger = new Ledger { On = DateTime.Now, By = actor.FullName, Secret = _settings.Protect(password) };
            var random = new Random(20261007); // the same demo every time
            var now = DateTime.Now;
            PasswordHasher.Create(password, out byte[] hash, out byte[] salt);

            await using var transaction = await _db.Database.BeginTransactionAsync();

            // support lists: reuse what exists under the same name
            var types = new List<SupportType>();
            foreach (var name in SupportTypes)
            {
                var row = await _db.SupportTypes.FirstOrDefaultAsync(x => x.SType == name);
                if (row == null) { row = new SupportType { SType = name }; _db.SupportTypes.Add(row); await _db.SaveChangesAsync(); ledger.SupportTypes.Add(row.SupportTypeId); }
                types.Add(row);
            }

            var categories = new List<SupportCatagory>();
            foreach (var name in Categories)
            {
                var row = await _db.SupportCatagories.FirstOrDefaultAsync(x => x.SCatagory == name);
                if (row == null) { row = new SupportCatagory { SCatagory = name }; _db.SupportCatagories.Add(row); await _db.SaveChangesAsync(); ledger.Categories.Add(row.SupportCatagoryId); }
                categories.Add(row);
            }

            var subCategories = new List<SupportSubCatagory>();
            foreach (var name in SubCategories)
            {
                var row = await _db.SupportSubCatagories.FirstOrDefaultAsync(x => x.SubCatagory == name);
                if (row == null) { row = new SupportSubCatagory { SubCatagory = name }; _db.SupportSubCatagories.Add(row); await _db.SaveChangesAsync(); ledger.SubCategories.Add(row.SupportSubCatagoryId); }
                subCategories.Add(row);
            }

            var sections = new List<AffectedSection>();
            foreach (var name in Sections)
            {
                var row = await _db.AffectedSectionss.FirstOrDefaultAsync(x => x.ASection == name);
                if (row == null) { row = new AffectedSection { ASection = name }; _db.AffectedSectionss.Add(row); await _db.SaveChangesAsync(); ledger.Sections.Add(row.AffectedSectionId); }
                sections.Add(row);
            }

            // XFL staff (they belong to the house of the administrator who loads the data)
            var number = 1;
            User NewUser(string userName, string fullName, string designation, Role role, int houseId, int branchId)
            {
                var columns = Rbac.Columns(role);
                var user = new User
                {
                    FullName = fullName, UserName = userName, Email = userName + MailDomain, PhonNumber = "0000" + (100000 + number).ToString(),
                    Designation = designation, EmployeeId = "D" + number.ToString("000"), BrokerageHouseName = houseId, BrokerageHouseAcronym = houseId,
                    Branch = branchId, Department = columns.Position, UType = columns.IsXflStaff, UCatagory = columns.IsAdmin,
                    PasswordHash = hash, PasswordSalt = salt, VerifiedAt = now.AddDays(-70), UStatus = true, Terms = true
                };
                number++;
                _db.Users.Add(user);
                return user;
            }

            var staff = Staff.Select(person => NewUser(person.UserName, person.FullName, person.Designation, person.Role, staffHouse.BrokerageHouseName, staffHouse.Branch)).ToList();
            await _db.SaveChangesAsync();
            ledger.Users.AddRange(staff.Select(user => user.Id));
            var manager = staff[0];
            var engineers = staff.Skip(1).ToList();

            // houses, branches, their people
            var raisers = new List<User>();
            var houseRows = new List<Brokerage>();
            for (var h = 0; h < Houses.Length; h++)
            {
                var house = new Brokerage { BrokerageHouseName = Houses[h].Name, BrokerageHouseAcronym = Houses[h].Acronym };
                _db.Brokerages.Add(house);
                await _db.SaveChangesAsync();
                ledger.Houses.Add(house.BrokerageId);
                houseRows.Add(house);

                var branches = Houses[h].Branches.Select(name => new Branchh { BranchName = name, BrokerageId = house.BrokerageId }).ToList();
                _db.Branchhs.AddRange(branches);
                await _db.SaveChangesAsync();
                ledger.Branches.AddRange(branches.Select(branch => branch.BranchId));

                var key = Houses[h].Acronym.ToLowerInvariant();
                var people = new List<User>();
                for (var p = 0; p < HousePeople[h].Length; p++)
                {
                    var admin = p == 0;
                    people.Add(NewUser("demo." + key + (admin ? ".admin" : ".user" + p), HousePeople[h][p],
                        admin ? "Head of Operations" : p == 1 ? "Senior Dealer" : "Operations Officer",
                        admin ? Role.HouseAdmin : Role.HouseUser, house.BrokerageId, branches[p % branches.Count].BranchId));
                }

                // one registration that still waits for activation, so that page has something to show
                if (h == 0)
                {
                    var waiting = NewUser("demo." + key + ".new", "Nabila Reza", "Dealer", Role.HouseUser, house.BrokerageId, branches[0].BranchId);
                    waiting.VerifiedAt = null;
                    waiting.VerificationToken = Guid.NewGuid().ToString("N");
                    people.Add(waiting);
                }

                await _db.SaveChangesAsync();
                ledger.Users.AddRange(people.Select(user => user.Id));
                raisers.AddRange(people.Where(user => user.VerifiedAt != null));
            }

            // tickets
            var statuses = Spread.SelectMany(item => Enumerable.Repeat(item.Status, item.Count)).OrderBy(_ => random.Next()).ToList();
            var serial = Houses.ToDictionary(house => house.Acronym, house => 0);
            var created = new List<IssueTable>();
            var history = new List<(IssueTable Issue, DateTime At, User By, string Action, string Text)>();
            for (var t = 0; t < statuses.Count; t++)
            {
                var status = statuses[t];
                var problem = Problems[t % Problems.Length];
                var raiser = raisers[random.Next(raisers.Count)];
                var house = houseRows.First(row => row.BrokerageId == raiser.BrokerageHouseName);
                var closed = status == TicketStatus.Closed;
                // closed tickets are older; unassigned ones are recent
                var ageDays = closed ? random.Next(12, 75) : status == TicketStatus.Unassigned ? random.Next(0, 4) : random.Next(1, 25);
                var raisedAt = now.AddDays(-ageDays).AddMinutes(-random.Next(30, 600));
                var priority = TicketService.Priorities[new[] { 0, 1, 1, 1, 2, 2 }[random.Next(6)]];
                serial[house.BrokerageHouseAcronym]++;

                var issue = new IssueTable
                {
                    UserId = raiser.Id, BrokerageId = house.BrokerageId, TDate = raisedAt,
                    TNumber = house.BrokerageHouseAcronym + "_" + serial[house.BrokerageHouseAcronym].ToString("D7"),
                    Priority = priority, ITitle = problem.Title + (t >= Problems.Length ? " (" + (t / Problems.Length + 1) + ")" : string.Empty),
                    Details = "<p>" + problem.Details + "</p>",
                    SupportTypeId = types[problem.Type].SupportTypeId, SupportCatagoryId = categories[problem.Category].SupportCatagoryId,
                    SupportSubCatagoryId = subCategories[problem.Category].SupportSubCatagoryId, AffectedSectionId = sections[problem.Section].AffectedSectionId,
                    IStatus = status, AssignBy = null, ApproveBy = null, UpdatedBy = null
                };
                history.Add((issue, raisedAt, raiser, AuditActions.TicketCreate, "Raised the ticket “" + issue.ITitle + "”, priority " + priority));

                if (status != TicketStatus.Unassigned)
                {
                    var engineer = engineers[random.Next(engineers.Count)];
                    var step = raisedAt.AddMinutes(random.Next(20, 300));
                    issue.AssignedToId = engineer.Id;
                    issue.AssignBy = engineer.FullName;
                    issue.AssignOn = step;
                    issue.ApproveOn = step;
                    issue.ApproveBy = manager.FullName;
                    history.Add((issue, step, manager, AuditActions.TicketAssign, "Assigned to " + engineer.FullName));

                    // walk the ticket through the statuses up to the one it has now
                    var path = new List<string>();
                    var order = new[] { TicketStatus.InProgress, TicketStatus.Review, TicketStatus.Done, TicketStatus.Deployed, TicketStatus.Closed };
                    if (status == TicketStatus.Pending) { path.Add(TicketStatus.InProgress); path.Add(TicketStatus.Pending); }
                    else if (status != TicketStatus.Assigned) { path.AddRange(order.Take(Array.IndexOf(order, status) + 1)); }

                    var before = TicketStatus.Assigned;
                    foreach (var next in path)
                    {
                        step = step.AddMinutes(random.Next(60, 60 * 30));
                        if (step > now) { step = now.AddMinutes(-random.Next(5, 90)); }
                        var by = next == TicketStatus.Done || next == TicketStatus.Closed ? manager : engineer;
                        history.Add((issue, step, by, AuditActions.TicketStatus,
                            (next == TicketStatus.Closed ? "Closed the ticket" : "Status " + TicketStatus.Name(next)) + " (was " + TicketStatus.Name(before) + ")"));
                        before = next;
                        if (next == TicketStatus.Closed) { issue.ClosedOn = step; issue.ClosedBy = by.FullName; }
                    }

                    if (path.Count > 0)
                    {
                        issue.Comments = EngineerNotes[random.Next(EngineerNotes.Length)];
                        issue.UpdatedOn = step;
                        issue.UpdatedBy = history[history.Count - 1].By.FullName;
                    }
                }

                created.Add(issue);
            }

            _db.Issues.AddRange(created);
            await _db.SaveChangesAsync();
            ledger.Issues.AddRange(created.Select(issue => issue.IssueId));

            foreach (var line in history.OrderBy(item => item.At))
            {
                _db.AuditLogs.Add(new AuditLog
                {
                    At = line.At, UserId = line.By.Id, UserName = line.By.FullName + " (" + line.By.UserName + ")", Role = Rbac.Label(Rbac.RoleOf(line.By)),
                    BrokerageId = line.Issue.BrokerageId, Action = line.Action, EntityType = "Ticket", EntityId = line.Issue.IssueId,
                    EntityLabel = line.Issue.TNumber, Details = line.Text
                });
            }

            // to-dos for every demo account and for the administrator who loads the data
            var todos = new List<Todo>();
            foreach (var owner in staff.Concat(raisers).Append(await _db.Users.FirstAsync(u => u.Id == actor.Id)))
            {
                var count = random.Next(2, 5);
                for (var i = 0; i < count; i++)
                {
                    todos.Add(new Todo
                    {
                        Todoname = TodoTexts[random.Next(TodoTexts.Length)], Status = new[] { "In progress", "In progress", "Done", "Canceled" }[random.Next(4)],
                        CreatedOn = now.AddDays(-random.Next(0, 20)).AddMinutes(-random.Next(0, 500)), UserId = owner.Id, BrokerageId = owner.BrokerageHouseName
                    });
                }
            }

            _db.Todos.AddRange(todos);
            await _db.SaveChangesAsync();
            ledger.Todos.AddRange(todos.Select(todo => todo.Id));

            // notifications, so the bell has content: the newest events of each person's tickets
            void Notice(int userId, DateTime at, string kind, string title, string body, IssueTable issue, bool read)
            {
                _db.Notifications.Add(new Notification
                {
                    UserId = userId, At = at, Kind = kind, Title = title, Body = body, LinkAction = "TicketView", LinkId = issue.IssueId,
                    ReadAt = read ? at.AddMinutes(30) : null
                });
            }

            var newest = created.OrderByDescending(issue => issue.TDate).ToList();
            foreach (var issue in newest.Where(issue => issue.IStatus == TicketStatus.Unassigned).Take(5))
            {
                var raiser = raisers.First(user => user.Id == issue.UserId);
                foreach (var reader in new[] { actor.Id, manager.Id })
                {
                    Notice(reader, issue.TDate, NotificationEvents.TicketCreated, "New ticket " + issue.TNumber + ": " + issue.ITitle,
                        "Raised by " + raiser.FullName + ", priority " + issue.Priority + ".", issue, false);
                }
            }

            foreach (var issue in newest.Where(issue => issue.AssignedToId != null).Take(24))
            {
                var isNew = issue.IStatus == TicketStatus.Assigned || issue.IStatus == TicketStatus.InProgress;
                Notice(issue.AssignedToId!.Value, issue.AssignOn ?? issue.TDate, NotificationEvents.TicketAssigned,
                    "Ticket " + issue.TNumber + " is assigned to you", "“" + issue.ITitle + "”, priority " + issue.Priority + " by " + manager.FullName + ".", issue, !isNew);
                Notice(issue.UserId, issue.UpdatedOn ?? issue.AssignOn ?? issue.TDate,
                    TicketStatus.NeedsClosePermission(issue.IStatus) ? NotificationEvents.TicketClosed : NotificationEvents.TicketStatus,
                    "Ticket " + issue.TNumber + ": " + TicketStatus.Name(issue.IStatus), "“" + issue.ITitle + "” was updated by " + (issue.UpdatedBy ?? issue.AssignBy) + ".",
                    issue, TicketStatus.IsClosed(issue.IStatus));
            }

            _audit.Add(AuditActions.SystemDemoData, actor, Rbac.Label(Rbac.RoleOf(actor)), null, "System", null, "Demo data",
                "Loaded the demo data: " + ledger.Houses.Count + " brokerage houses, " + ledger.Users.Count + " accounts, " + ledger.Issues.Count + " tickets, " + ledger.Todos.Count + " to-dos");
            _settings.Stage(_db, new Dictionary<string, string?> { [SettingsStore.DemoData] = JsonSerializer.Serialize(ledger) }, actor.FullName);
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
            _settings.Reload();

            return new DemoResult
            {
                Ok = true,
                Message = "Demo data loaded: " + ledger.Houses.Count + " brokerage houses, " + ledger.Users.Count + " accounts and " + ledger.Issues.Count
                    + " tickets. The accounts and their password are listed below."
            };
        }

        // ---- remove --------------------------------------------------------------------------------

        public async Task<DemoResult> RemoveAsync(User actor)
        {
            await Gate.WaitAsync();
            try
            {
                _settings.Reload();
                return await RemoveLockedAsync(actor);
            }
            finally
            {
                Gate.Release();
            }
        }

        private async Task<DemoResult> RemoveLockedAsync(User actor)
        {
            var ledger = ReadLedger();
            if (ledger == null)
            {
                return new DemoResult { Message = "No demo data is loaded." };
            }

            if (ledger.Users.Contains(actor.Id))
            {
                return new DemoResult { Message = "You are signed in with a demo account. Sign in with your own account to remove the demo data." };
            }

            await using var transaction = await _db.Database.BeginTransactionAsync();
            var kept = new List<string>();

            // The list says which rows were created; before anything is deleted each row must still look like demo
            // data. (The list is only a setting: it can end up in another database, and a demo account can have been
            // handed to a real person and renamed.)
            var listed = await _db.Users.Where(user => ledger.Users.Contains(user.Id)).ToListAsync();
            var demoUsers = listed.Where(IsDemoAccount).ToList();
            foreach (var user in listed.Where(user => !IsDemoAccount(user)))
            {
                kept.Add("the account " + user.UserName);
            }

            var demoUserIds = demoUsers.Select(user => user.Id).ToList();
            var demoHouses = (await _db.Brokerages.Where(b => ledger.Houses.Contains(b.BrokerageId)).ToListAsync())
                .Where(house => Houses.Any(demo => demo.Acronym == house.BrokerageHouseAcronym)).ToList();
            var demoHouseIds = demoHouses.Select(house => house.BrokerageId).ToList();
            kept.AddRange((await _db.Brokerages.Where(b => ledger.Houses.Contains(b.BrokerageId)).ToListAsync())
                .Where(house => !demoHouseIds.Contains(house.BrokerageId)).Select(house => house.BrokerageHouseName));
            ledger.Users = demoUserIds;

            // tickets: the demo tickets (still in a demo house), and whatever else the demo accounts raised while people tried the system
            var demoIssues = await _db.Issues.Include(issue => issue.attachment)
                .Where(issue => (ledger.Issues.Contains(issue.IssueId) && demoHouseIds.Contains(issue.BrokerageId)) || demoUserIds.Contains(issue.UserId)).ToListAsync();
            var issueIds = demoIssues.Select(issue => issue.IssueId).ToList();
            var files = demoIssues.SelectMany(issue => issue.attachment ?? new List<Attachment>()).ToList();

            _db.Notifications.RemoveRange(await _db.Notifications
                .Where(n => ledger.Users.Contains(n.UserId) || (n.LinkAction == "TicketView" && n.LinkId != null && issueIds.Contains(n.LinkId.Value))).ToListAsync());
            _db.NotificationDeliveries.RemoveRange(await _db.NotificationDeliveries.Where(d => d.UserId != null && ledger.Users.Contains(d.UserId.Value)).ToListAsync());
            _db.NotificationPreferences.RemoveRange(await _db.NotificationPreferences.Where(p => ledger.Users.Contains(p.UserId)).ToListAsync());
            _db.Todos.RemoveRange((await _db.Todos.Where(todo => ledger.Todos.Contains(todo.Id) || ledger.Users.Contains(todo.UserId)).ToListAsync())
                .Where(todo => demoUserIds.Contains(todo.UserId) || TodoTexts.Contains(todo.Todoname)));
            _db.Attachments.RemoveRange(files);
            _db.Issues.RemoveRange(demoIssues);
            await _db.SaveChangesAsync();

            // real tickets that were given to a demo engineer are unassigned again
            var orphaned = await _db.Issues.Where(issue => issue.AssignedToId != null && ledger.Users.Contains(issue.AssignedToId.Value)).ToListAsync();
            foreach (var issue in orphaned)
            {
                issue.AssignedToId = null;
                issue.AssignBy = null;
                issue.AssignOn = null;
                issue.ApproveBy = null;
                issue.ApproveOn = null;
                if (!TicketStatus.IsClosed(issue.IStatus)) { issue.IStatus = TicketStatus.Unassigned; }
            }

            _db.Users.RemoveRange(await _db.Users.Where(user => ledger.Users.Contains(user.Id)).ToListAsync());
            await _db.SaveChangesAsync();

            // houses and branches: only when nothing real was put into them meanwhile
            foreach (var house in demoHouses)
            {
                if (await _db.Users.AnyAsync(u => u.BrokerageHouseName == house.BrokerageId) || await _db.Issues.AnyAsync(i => i.BrokerageId == house.BrokerageId))
                {
                    kept.Add(house.BrokerageHouseName);
                    continue;
                }

                _db.Branchhs.RemoveRange(await _db.Branchhs.Where(branch => branch.BrokerageId == house.BrokerageId).ToListAsync());
                _db.Brokerages.Remove(house);
            }

            await _db.SaveChangesAsync();

            // support lists that the demo created (still under the demo's name) and nothing uses any more
            _db.SupportTypes.RemoveRange((await _db.SupportTypes.Where(x => ledger.SupportTypes.Contains(x.SupportTypeId) && !_db.Issues.Any(i => i.SupportTypeId == x.SupportTypeId)).ToListAsync())
                .Where(x => SupportTypes.Contains(x.SType)));
            _db.SupportCatagories.RemoveRange((await _db.SupportCatagories.Where(x => ledger.Categories.Contains(x.SupportCatagoryId) && !_db.Issues.Any(i => i.SupportCatagoryId == x.SupportCatagoryId)).ToListAsync())
                .Where(x => Categories.Contains(x.SCatagory)));
            _db.SupportSubCatagories.RemoveRange((await _db.SupportSubCatagories.Where(x => ledger.SubCategories.Contains(x.SupportSubCatagoryId) && !_db.Issues.Any(i => i.SupportSubCatagoryId == x.SupportSubCatagoryId)).ToListAsync())
                .Where(x => SubCategories.Contains(x.SubCatagory)));
            _db.AffectedSectionss.RemoveRange((await _db.AffectedSectionss.Where(x => ledger.Sections.Contains(x.AffectedSectionId) && !_db.Issues.Any(i => i.AffectedSectionId == x.AffectedSectionId)).ToListAsync())
                .Where(x => Sections.Contains(x.ASection)));

            _audit.Add(AuditActions.SystemDemoData, actor, Rbac.Label(Rbac.RoleOf(actor)), null, "System", null, "Demo data",
                "Removed the demo data: " + ledger.Users.Count + " accounts, " + demoIssues.Count + " tickets"
                + (kept.Count == 0 ? string.Empty : ". Kept because they are no longer demo data, or hold other accounts or tickets: " + string.Join(", ", kept)));
            _settings.Stage(_db, new Dictionary<string, string?> { [SettingsStore.DemoData] = null }, actor.FullName);
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
            _settings.Reload();

            // the files of the removed tickets, once the rows are really gone
            foreach (var file in files)
            {
                try
                {
                    if (!string.IsNullOrEmpty(file.AttachmentLoc) && File.Exists(file.AttachmentLoc) && !await _db.Attachments.AnyAsync(a => a.AttachmentLoc == file.AttachmentLoc))
                    {
                        File.Delete(file.AttachmentLoc);
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }

            return new DemoResult
            {
                Ok = true,
                Message = "The demo data is removed." + (kept.Count == 0 ? string.Empty
                    : " This stays, because it is no longer demo data or holds accounts or tickets that are not: " + string.Join(", ", kept) + ".")
            };
        }
    }
}
