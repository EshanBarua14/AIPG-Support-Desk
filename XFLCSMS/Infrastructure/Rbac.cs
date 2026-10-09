using Microsoft.AspNetCore.Mvc;
using XFLCSMS.Models.Register;

namespace XFLCSMS.Infrastructure
{
    /// <summary>The five roles. A user has exactly one; it follows from three columns of the user (see <see cref="Rbac.RoleOf(User)"/>).</summary>
    public enum Role
    {
        /// <summary>Administrator of AIPG: everything, for every brokerage house.</summary>
        PlatformAdmin,
        /// <summary>AIPG support manager: every ticket, assigns the engineers.</summary>
        SupportManager,
        /// <summary>AIPG support engineer: sees every ticket, works on the tickets assigned to him.</summary>
        SupportEngineer,
        /// <summary>Administrator of one brokerage house: its tickets, users and branches.</summary>
        HouseAdmin,
        /// <summary>User of a brokerage house: the tickets he raised himself.</summary>
        HouseUser
    }

    /// <summary>What a role may do. The table in <see cref="Rbac"/> is the one place that grants these.</summary>
    public enum Permission
    {
        /// <summary>See and open the tickets of every brokerage house.</summary>
        TicketsAll,
        /// <summary>See and open every ticket of their own brokerage house.</summary>
        TicketsHouse,
        /// <summary>Raise a ticket.</summary>
        TicketCreate,
        /// <summary>Change the open tickets colleagues of the own brokerage house raised (title, details, priority, files).</summary>
        TicketEditHouse,
        /// <summary>Assign, reassign or unassign any ticket.</summary>
        TicketAssign,
        /// <summary>Take an unassigned ticket, give an own ticket back.</summary>
        TicketTake,
        /// <summary>Set status and comments on any ticket (without it: on the tickets assigned to oneself).</summary>
        TicketWorkAny,
        /// <summary>Set a ticket to Deployed or Closed, and reopen a closed ticket.</summary>
        TicketClose,
        /// <summary>Delete a ticket.</summary>
        TicketDelete,
        /// <summary>See how many tickets each engineer has.</summary>
        Workload,
        /// <summary>Create and manage the accounts of everybody.</summary>
        UsersAll,
        /// <summary>Create and manage the accounts of the own brokerage house.</summary>
        UsersHouse,
        /// <summary>Add, rename and remove the branches of the own brokerage house.</summary>
        BranchesHouse,
        /// <summary>Brokerage houses, all branches, support types, categories, sub-categories, affected sections.</summary>
        MasterData,
        /// <summary>To-dos of all users.</summary>
        TeamTodos,
        /// <summary>The whole audit trail.</summary>
        AuditAll,
        /// <summary>The audit trail of tickets (all brokerage houses).</summary>
        AuditTickets,
        /// <summary>The audit trail of the own brokerage house.</summary>
        AuditHouse,
        /// <summary>The system health page.</summary>
        SystemHealth,
        /// <summary>Notification channels (e-mail, SMS), which event tells whom, demo data.</summary>
        SystemSettings,
        /// <summary>Change what each role may do (this table).</summary>
        PermissionsEdit,

        // Added with version 3.3. New permissions go at the end: the stored table names the ones it knew (see Rbac.Parse).

        /// <summary>The service targets: times per priority, working hours, holidays.</summary>
        ServiceTargets,
        /// <summary>Write and change the canned replies everybody of AIPG can use.</summary>
        CannedReplies,
        /// <summary>The service report: response and solution times, targets met, ratings.</summary>
        ServiceReport,

        // Added with version 3.4.

        /// <summary>Write, change, publish and remove the articles of the knowledge base. (Reading needs no permission.)</summary>
        Knowledge,
        /// <summary>The rules that act by themselves: who gets a new ticket, closing after "Deployed", reminders while "Pending".</summary>
        Automation
    }

    /// <summary>
    /// Role based access control in one place: which role a user has, which area (controller) and session key
    /// belong to it, and what it may do. Controllers ask <see cref="Can"/> (or carry <see cref="RequirePermissionAttribute"/>),
    /// views ask the same table through Ui.Role(...).Can(...), so a button is shown exactly when its action is allowed.
    /// The table can be changed on the permissions page (stored in AppSettings, key "rbac.grants") within the limits
    /// of <see cref="Grantable"/> and <see cref="IsFixed"/>.
    /// </summary>
    public static class Rbac
    {
        /// <summary>Stored in Users.Department for the administrator of a brokerage house.</summary>
        public const string HouseAdminPosition = "House Admin";
        public const string HouseUserPosition = "Maker";
        public const string EngineerPosition = "Support Engineer";
        /// <summary>The spelling is the one in the existing data.</summary>
        public const string ManagerPosition = "Support Maneger";

        // Declared first: the tables below use it while the class is being set up.
        private static readonly Role[] Staff = { Role.PlatformAdmin, Role.SupportManager, Role.SupportEngineer };

        public static readonly Role[] AllRoles = { Role.PlatformAdmin, Role.SupportManager, Role.SupportEngineer, Role.HouseAdmin, Role.HouseUser };

        /// <summary>What each role may do when nobody has changed anything (a fresh installation, and "Reset to defaults").</summary>
        public static readonly IReadOnlyDictionary<Role, Permission[]> Defaults = new Dictionary<Role, Permission[]>
        {
            [Role.PlatformAdmin] = new[]
            {
                Permission.TicketsAll, Permission.TicketAssign, Permission.TicketWorkAny, Permission.TicketClose, Permission.TicketDelete, Permission.Workload,
                Permission.UsersAll, Permission.MasterData, Permission.TeamTodos, Permission.AuditAll, Permission.SystemHealth,
                Permission.SystemSettings, Permission.PermissionsEdit,
                Permission.ServiceTargets, Permission.CannedReplies, Permission.ServiceReport,
                Permission.Knowledge, Permission.Automation
            },
            [Role.SupportManager] = new[]
            {
                Permission.TicketsAll, Permission.TicketCreate, Permission.TicketAssign, Permission.TicketWorkAny, Permission.TicketClose, Permission.Workload,
                Permission.AuditTickets, Permission.CannedReplies, Permission.ServiceReport,
                Permission.Knowledge, Permission.Automation
            },
            [Role.SupportEngineer] = new[]
            {
                Permission.TicketsAll, Permission.TicketCreate, Permission.TicketTake, Permission.TicketClose,
                Permission.Knowledge
            },
            [Role.HouseAdmin] = new[]
            {
                Permission.TicketsHouse, Permission.TicketCreate, Permission.TicketEditHouse, Permission.UsersHouse, Permission.BranchesHouse, Permission.AuditHouse,
                Permission.ServiceReport
            },
            [Role.HouseUser] = new[]
            {
                Permission.TicketCreate
            }
        };

        /// <summary>
        /// The roles a permission can be given to at all. This is what keeps the brokerage houses apart whatever is
        /// ticked on the permissions page: a permission that reaches across houses can only go to AIPG roles, and the
        /// house permissions only to the house admin.
        ///
        /// Two deliberate limits:
        ///  - A house user never gets more than his own tickets. Anybody can register for any brokerage house and
        ///    activate the account with the token from his own e-mail, so "house user" proves nothing about a person;
        ///    a house admin is appointed by an administrator.
        ///  - The settings of the system (mail server, SMS gateway, demo data) stay with the platform admin: whoever
        ///    controls the mail server can read password-reset tokens.
        /// </summary>
        public static readonly IReadOnlyDictionary<Permission, Role[]> Grantable = new Dictionary<Permission, Role[]>
        {
            [Permission.TicketsAll] = Staff,
            [Permission.TicketsHouse] = new[] { Role.HouseAdmin },
            [Permission.TicketCreate] = new[] { Role.SupportManager, Role.SupportEngineer, Role.HouseAdmin, Role.HouseUser },
            [Permission.TicketEditHouse] = new[] { Role.HouseAdmin },
            [Permission.TicketAssign] = Staff,
            [Permission.TicketTake] = new[] { Role.SupportEngineer },
            [Permission.TicketWorkAny] = Staff,
            [Permission.TicketClose] = Staff,
            [Permission.TicketDelete] = new[] { Role.PlatformAdmin, Role.SupportManager },
            [Permission.Workload] = Staff,
            [Permission.UsersAll] = new[] { Role.PlatformAdmin, Role.SupportManager },
            [Permission.UsersHouse] = new[] { Role.HouseAdmin },
            [Permission.BranchesHouse] = new[] { Role.HouseAdmin },
            [Permission.MasterData] = new[] { Role.PlatformAdmin, Role.SupportManager },
            [Permission.TeamTodos] = new[] { Role.PlatformAdmin, Role.SupportManager },
            [Permission.AuditAll] = new[] { Role.PlatformAdmin, Role.SupportManager },
            [Permission.AuditTickets] = Staff,
            [Permission.AuditHouse] = new[] { Role.HouseAdmin },
            [Permission.SystemHealth] = new[] { Role.PlatformAdmin, Role.SupportManager },
            [Permission.SystemSettings] = new[] { Role.PlatformAdmin },
            [Permission.PermissionsEdit] = new[] { Role.PlatformAdmin },
            [Permission.ServiceTargets] = new[] { Role.PlatformAdmin, Role.SupportManager },
            [Permission.CannedReplies] = Staff,
            // a house admin sees the report of the own house only (CsmsController.ServiceReport)
            [Permission.ServiceReport] = new[] { Role.PlatformAdmin, Role.SupportManager, Role.SupportEngineer, Role.HouseAdmin },
            [Permission.Knowledge] = Staff,
            // the rules decide who gets which ticket: with the people who assign tickets by hand
            [Permission.Automation] = new[] { Role.PlatformAdmin, Role.SupportManager }
        };

        /// <summary>
        /// What can never be taken away: the platform admin keeps the accounts and this table itself,
        /// so there is always somebody who can repair a wrong setting.
        /// </summary>
        public static bool IsFixed(Role role, Permission permission)
        {
            return role == Role.PlatformAdmin && (permission == Permission.PermissionsEdit || permission == Permission.UsersAll);
        }

        public static bool CanBeGranted(Role role, Permission permission)
        {
            return Grantable.TryGetValue(permission, out var roles) && roles.Contains(role);
        }

        // The table in force. Replaced as a whole (never changed in place), so readers need no lock.
        private static volatile Dictionary<Role, HashSet<Permission>> _granted = Build(null);

        private static Dictionary<Role, HashSet<Permission>> Build(IReadOnlyDictionary<Role, IEnumerable<Permission>>? chosen)
        {
            var table = new Dictionary<Role, HashSet<Permission>>();
            foreach (var role in new[] { Role.PlatformAdmin, Role.SupportManager, Role.SupportEngineer, Role.HouseAdmin, Role.HouseUser })
            {
                var wanted = chosen != null && chosen.TryGetValue(role, out var picked) ? picked : Defaults[role];
                var set = new HashSet<Permission>(wanted.Where(permission => CanBeGranted(role, permission)));
                foreach (var permission in Enum.GetValues<Permission>())
                {
                    if (IsFixed(role, permission)) { set.Add(permission); }
                }

                table[role] = set;
            }

            return table;
        }

        /// <summary>
        /// Puts a table in force (from the permissions page, or read from the database at start-up). Anything that
        /// cannot be granted to a role is dropped and the fixed permissions are added, whatever was asked for.
        /// Null means the defaults.
        /// </summary>
        public static void Apply(IReadOnlyDictionary<Role, IEnumerable<Permission>>? chosen)
        {
            _granted = Build(chosen);
        }

        /// <summary>The table as text, for storing it: "Role=Permission,Permission;Role=...".</summary>
        public static string Serialize()
        {
            return Serialize(_granted);
        }

        private static string Serialize(Dictionary<Role, HashSet<Permission>> table)
        {
            // "Known" lists every permission that existed when the table was saved. A later version reads from it
            // which permissions are new since then, and gives exactly those to their default roles (see Parse).
            return string.Join(";", AllRoles.Select(role => role + "=" + string.Join(",", table[role].OrderBy(p => p))))
                + ";" + KnownEntry + "=" + string.Join(",", Enum.GetValues<Permission>());
        }

        private const string KnownEntry = "Known";

        /// <summary>The permissions of version 3.0 to 3.2: what a stored table without a "Known" entry knew.</summary>
        private static readonly Permission[] KnownBefore33 = Enum.GetValues<Permission>().Where(permission => permission <= Permission.PermissionsEdit).ToArray();

        /// <summary>
        /// What a wish becomes once the limits are applied (nothing a role cannot hold, the fixed permissions added):
        /// the permissions per role, and the text to store. Null as the wish gives the defaults.
        /// </summary>
        public static (Dictionary<Role, HashSet<Permission>> Table, string Text, bool IsDefault) Preview(IReadOnlyDictionary<Role, IEnumerable<Permission>>? chosen)
        {
            var table = Build(chosen);
            var defaults = Build(null);
            return (table, Serialize(table), AllRoles.All(role => table[role].SetEquals(defaults[role])));
        }

        /// <summary>Name of a permission as shown on the permissions page.</summary>
        public static string NameOf(Permission permission)
        {
            foreach (var entry in Catalogue)
            {
                if (entry.Permission == permission) { return entry.Name; }
            }

            return permission.ToString();
        }

        /// <summary>Reads what <see cref="Serialize"/> wrote. Unknown names (an older or newer version) are skipped.</summary>
        public static Dictionary<Role, IEnumerable<Permission>>? Parse(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var chosen = new Dictionary<Role, IEnumerable<Permission>>();
            var known = new HashSet<Permission>(KnownBefore33);
            foreach (var part in text.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var pair = part.Split('=', 2);
                if (pair.Length == 2 && pair[0] == KnownEntry)
                {
                    foreach (var name in pair[1].Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (Enum.TryParse<Permission>(name, out var knownPermission) && Enum.IsDefined(knownPermission)) { known.Add(knownPermission); }
                    }

                    continue;
                }

                if (pair.Length != 2 || !Enum.TryParse<Role>(pair[0], out var role) || !Enum.IsDefined(role))
                {
                    continue;
                }

                var permissions = new List<Permission>();
                foreach (var name in pair[1].Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (Enum.TryParse<Permission>(name, out var permission) && Enum.IsDefined(permission))
                    {
                        permissions.Add(permission);
                    }
                }

                chosen[role] = permissions;
            }

            // A permission the stored table did not know was not decided by anybody: it goes to its default roles.
            // (Without this, a table saved on the permissions page would lock every later feature away from everyone.)
            foreach (var role in chosen.Keys.ToList())
            {
                var fresh = Defaults[role].Where(permission => !known.Contains(permission)).ToList();
                if (fresh.Count > 0)
                {
                    chosen[role] = chosen[role].Concat(fresh).Distinct().ToList();
                }
            }

            return chosen.Count == 0 ? null : chosen;
        }

        public static bool Can(Role role, Permission permission)
        {
            return _granted[role].Contains(permission);
        }

        public static IReadOnlyCollection<Permission> PermissionsOf(Role role)
        {
            return _granted[role];
        }

        /// <summary>Does the current table differ from the defaults?</summary>
        public static bool IsCustomised => AllRoles.Any(role => !_granted[role].SetEquals(Build(null)[role]));

        /// <summary>Groups, names and explanations of the permissions, in the order of the permissions page.</summary>
        public static readonly (string Group, Permission Permission, string Name, string About)[] Catalogue =
        {
            ("Tickets", Permission.TicketsAll, "See all tickets", "Every ticket of every brokerage house. Without it, AIPG staff see the tickets assigned to them and the ones they raised."),
            ("Tickets", Permission.TicketsHouse, "See the tickets of their house", "Every ticket raised by people of their own brokerage house. Without it: only the tickets they raised themselves."),
            ("Tickets", Permission.TicketCreate, "Raise tickets", "Create a ticket."),
            ("Tickets", Permission.TicketEditHouse, "Edit the tickets of their house", "Change title, details, priority and files of open tickets raised by colleagues of their own brokerage house. Everybody can always edit their own open tickets."),
            ("Tickets", Permission.TicketAssign, "Assign tickets", "Give any open ticket to a support engineer, to another one, or to nobody."),
            ("Tickets", Permission.TicketTake, "Take tickets", "Take an unassigned ticket, and give back a ticket of their own."),
            ("Tickets", Permission.TicketWorkAny, "Work on any ticket", "Set status and comments on every ticket. Without it: only on the tickets assigned to them."),
            ("Tickets", Permission.TicketClose, "Deploy, close and reopen", "Set a ticket to Deployed or Closed and reopen a closed ticket."),
            ("Tickets", Permission.TicketDelete, "Delete tickets", "Remove a ticket and its files for good."),
            ("Tickets", Permission.Workload, "Workload page", "How many tickets each engineer has, and what is waiting."),
            ("Accounts and master data", Permission.UsersAll, "Manage all accounts", "Create, change, disable and delete accounts of every house and of AIPG. Only a platform admin can make or change another platform admin."),
            ("Accounts and master data", Permission.UsersHouse, "Manage the accounts of their house", "Create, activate, change, disable and delete the house users and house admins of their own brokerage house."),
            ("Accounts and master data", Permission.BranchesHouse, "Manage the branches of their house", "Add, rename and remove branches of their own brokerage house."),
            ("Accounts and master data", Permission.MasterData, "Master data", "Brokerage houses, all branches, support types, categories, sub-categories and affected sections."),
            ("Accounts and master data", Permission.TeamTodos, "Team to-dos", "The to-do lists of all users and the report over them."),
            ("Service", Permission.ServiceReport, "Service report", "Response and solution times, targets met and missed, ratings. AIPG roles see all brokerage houses, a house admin the own house."),
            ("Service", Permission.CannedReplies, "Manage canned replies", "Write, change and remove the ready-made texts for replies. Every AIPG role can use them."),
            ("Service", Permission.ServiceTargets, "Service targets", "The time allowed for the first response and for the solution per priority, the working hours and the holidays."),
            ("Service", Permission.Knowledge, "Write the knowledge base", "Write, change, publish and remove articles. Reading needs no permission: published articles are for everybody, internal ones for AIPG roles."),
            ("Service", Permission.Automation, "Automation", "The rules that act by themselves: which engineer gets a new ticket, closing tickets some days after \u201cDeployed\u201d, reminders while a ticket is \u201cPending\u201d."),
            ("System", Permission.AuditAll, "Whole audit trail", "Every line: sign-ins, accounts, tickets, master data, settings."),
            ("System", Permission.AuditTickets, "Audit trail of tickets", "The lines about tickets, for all brokerage houses."),
            ("System", Permission.AuditHouse, "Audit trail of their house", "The lines of their own brokerage house: its tickets, accounts, branches and sign-ins."),
            ("System", Permission.SystemHealth, "System health", "The page that checks database, e-mail, files, security settings and the support queue."),
            ("System", Permission.SystemSettings, "Notification settings and demo data", "Mail server, SMS gateway, which event tells whom, loading and removing demo data. Only for the platform admin: whoever sets the mail server can read password-reset e-mails."),
            ("System", Permission.PermissionsEdit, "Change this table", "Decide what each role may do. Always with the platform admin.")
        };

        /// <summary>The roles in the order they are offered on the user forms: from the one with the smallest reach up.</summary>
        public static readonly Role[] RolesByReach = { Role.HouseUser, Role.HouseAdmin, Role.SupportEngineer, Role.SupportManager, Role.PlatformAdmin };

        /// <summary>
        /// The role of a user. Administrator wins; then AIPG staff by position; a brokerage house user is
        /// house administrator or house user. Anything unknown is the role with the fewest rights.
        /// </summary>
        public static Role RoleOf(bool isAdmin, bool isXflStaff, string? position)
        {
            if (isAdmin) { return Role.PlatformAdmin; }
            if (isXflStaff && position == ManagerPosition) { return Role.SupportManager; }
            if (isXflStaff && position == EngineerPosition) { return Role.SupportEngineer; }
            if (!isXflStaff && position == HouseAdminPosition) { return Role.HouseAdmin; }
            return Role.HouseUser;
        }

        public static Role RoleOf(User user)
        {
            return RoleOf(user.UCatagory, user.UType, user.Department);
        }

        /// <summary>The three columns that store a role (the reverse of <see cref="RoleOf(bool, bool, string?)"/>).</summary>
        public static (bool IsAdmin, bool IsXflStaff, string Position) Columns(Role role)
        {
            switch (role)
            {
                case Role.PlatformAdmin: return (true, true, HouseUserPosition);   // the platform administrator is AIPG staff
                case Role.SupportManager: return (false, true, ManagerPosition);
                case Role.SupportEngineer: return (false, true, EngineerPosition);
                case Role.HouseAdmin: return (false, false, HouseAdminPosition);
                default: return (false, false, HouseUserPosition);
            }
        }

        /// <summary>Name of the role as people read it.</summary>
        public static string Label(Role role)
        {
            switch (role)
            {
                case Role.PlatformAdmin: return "Platform admin";
                case Role.SupportManager: return "Support manager";
                case Role.SupportEngineer: return "Support engineer";
                case Role.HouseAdmin: return "House admin";
                default: return "House user";
            }
        }

        /// <summary>
        /// One line on what the role is for (shown on the user forms and the profile). While the role has its default
        /// permissions this is a written sentence; once they were changed on the permissions page it lists what the
        /// role really may do.
        /// </summary>
        public static string Summary(Role role)
        {
            var table = _granted;
            if (!table[role].SetEquals(Build(null)[role]))
            {
                var what = Catalogue.Where(entry => table[role].Contains(entry.Permission)).Select(entry => entry.Name.ToLowerInvariant()).ToList();
                var who = role == Role.PlatformAdmin ? "AIPG administrator."
                    : IsStaff(role) ? "AIPG staff."
                    : role == Role.HouseAdmin ? "Administrator of one brokerage house."
                    : "User of a brokerage house.";
                return who + (what.Count == 0 ? " Has no permissions at the moment." : " May: " + string.Join(", ", what) + ".");
            }

            switch (role)
            {
                case Role.PlatformAdmin: return "AIPG administrator. Everything: all tickets, all users, master data, audit trail, system health, settings.";
                case Role.SupportManager: return "AIPG staff. Sees every ticket, assigns engineers, sets status, sees the ticket audit trail.";
                case Role.SupportEngineer: return "AIPG staff. Sees every ticket, takes unassigned tickets, works on the tickets assigned to him.";
                case Role.HouseAdmin: return "Administrator of one brokerage house. All tickets, users and branches of that house, and its audit trail.";
                default: return "User of a brokerage house. Raises tickets and follows the tickets he raised.";
            }
        }

        /// <summary>Each role has its own controller (area of the site).</summary>
        public static string Controller(Role role)
        {
            switch (role)
            {
                case Role.PlatformAdmin: return "Admin";
                case Role.SupportManager: return "SupportManegar";
                case Role.SupportEngineer: return "SupportEngineer";
                case Role.HouseAdmin: return "HouseAdmin";
                default: return "Maker";
            }
        }

        public static Role? RoleOfController(string? controller)
        {
            foreach (var role in AllRoles)
            {
                if (string.Equals(Controller(role), controller, StringComparison.OrdinalIgnoreCase)) { return role; }
            }

            return null;
        }

        /// <summary>The session entry that holds the signed-in user of that role.</summary>
        public static string SessionKey(Role role)
        {
            switch (role)
            {
                case Role.PlatformAdmin: return SessionAuthorizeAttribute.Admin;
                case Role.SupportManager: return SessionAuthorizeAttribute.SupportManager;
                case Role.SupportEngineer: return SessionAuthorizeAttribute.SupportEngineer;
                case Role.HouseAdmin: return SessionAuthorizeAttribute.HouseAdmin;
                default: return SessionAuthorizeAttribute.Maker;
            }
        }

        /// <summary>AIPG staff: platform administrator, support manager, support engineer.</summary>
        public static bool IsStaff(Role role)
        {
            return role == Role.PlatformAdmin || role == Role.SupportManager || role == Role.SupportEngineer;
        }

        /// <summary>
        /// The brokerage house an account belongs to for the purpose of the audit trail and of house administration:
        /// AIPG staff accounts belong to the platform (null), everybody else to the house they registered for.
        /// </summary>
        public static int? HouseOf(User user)
        {
            return user.UType || user.UCatagory ? null : user.BrokerageHouseName;
        }
    }

    /// <summary>
    /// Put on an action of CsmsController (the base of the five role controllers): the action runs only for roles
    /// that hold one of the listed permissions. Everybody else gets the "your role does not allow this" page.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class RequirePermissionAttribute : Attribute
    {
        public RequirePermissionAttribute(params Permission[] anyOf)
        {
            AnyOf = anyOf;
        }

        public Permission[] AnyOf { get; }
    }
}
