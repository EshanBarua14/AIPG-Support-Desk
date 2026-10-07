using Microsoft.AspNetCore.Mvc;
using XFLCSMS.Models.Register;

namespace XFLCSMS.Infrastructure
{
    /// <summary>The five roles. A user has exactly one; it follows from three columns of the user (see <see cref="Rbac.RoleOf(User)"/>).</summary>
    public enum Role
    {
        /// <summary>Administrator of XFL: everything, for every brokerage house.</summary>
        PlatformAdmin,
        /// <summary>XFL support manager: every ticket, assigns the engineers.</summary>
        SupportManager,
        /// <summary>XFL support engineer: sees every ticket, works on the tickets assigned to him.</summary>
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
        /// <summary>See and open every ticket of the own brokerage house.</summary>
        TicketsHouse,
        /// <summary>Raise a ticket.</summary>
        TicketCreate,
        /// <summary>Assign, reassign or unassign any ticket.</summary>
        TicketAssign,
        /// <summary>Take an unassigned ticket, give an own ticket back.</summary>
        TicketTake,
        /// <summary>Set status and comments on any ticket (engineers: on their own tickets, see TicketService.CanWorkOn).</summary>
        TicketWorkAny,
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
        SystemHealth
    }

    /// <summary>
    /// Role based access control in one place: which role a user has, which area (controller) and session key
    /// belong to it, and what it may do. Controllers ask <see cref="Can"/> (or carry <see cref="RequirePermissionAttribute"/>),
    /// views ask the same table through Ui.Role(...).Can(...), so a button is shown exactly when its action is allowed.
    /// </summary>
    public static class Rbac
    {
        /// <summary>Stored in Users.Department for the administrator of a brokerage house.</summary>
        public const string HouseAdminPosition = "House Admin";
        public const string HouseUserPosition = "Maker";
        public const string EngineerPosition = "Support Engineer";
        /// <summary>The spelling is the one in the existing data.</summary>
        public const string ManagerPosition = "Support Maneger";

        private static readonly Dictionary<Role, HashSet<Permission>> Granted = new()
        {
            [Role.PlatformAdmin] = new()
            {
                Permission.TicketsAll, Permission.TicketAssign, Permission.TicketWorkAny, Permission.TicketDelete, Permission.Workload,
                Permission.UsersAll, Permission.MasterData, Permission.TeamTodos, Permission.AuditAll, Permission.SystemHealth
            },
            [Role.SupportManager] = new()
            {
                Permission.TicketsAll, Permission.TicketCreate, Permission.TicketAssign, Permission.TicketWorkAny, Permission.Workload,
                Permission.AuditTickets
            },
            [Role.SupportEngineer] = new()
            {
                Permission.TicketsAll, Permission.TicketCreate, Permission.TicketTake
            },
            [Role.HouseAdmin] = new()
            {
                Permission.TicketsHouse, Permission.TicketCreate, Permission.UsersHouse, Permission.BranchesHouse, Permission.AuditHouse
            },
            [Role.HouseUser] = new()
            {
                Permission.TicketCreate
            }
        };

        public static bool Can(Role role, Permission permission)
        {
            return Granted[role].Contains(permission);
        }

        public static IReadOnlyCollection<Permission> PermissionsOf(Role role)
        {
            return Granted[role];
        }

        public static readonly Role[] AllRoles = { Role.PlatformAdmin, Role.SupportManager, Role.SupportEngineer, Role.HouseAdmin, Role.HouseUser };

        /// <summary>The roles in the order they are offered on the user forms: from the one with the smallest reach up.</summary>
        public static readonly Role[] RolesByReach = { Role.HouseUser, Role.HouseAdmin, Role.SupportEngineer, Role.SupportManager, Role.PlatformAdmin };

        /// <summary>
        /// The role of a user. Administrator wins; then XFL staff by position; a brokerage house user is
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
                case Role.PlatformAdmin: return (true, true, HouseUserPosition);   // the platform administrator is XFL staff
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

        /// <summary>One line on what the role is for (shown on the user forms).</summary>
        public static string Summary(Role role)
        {
            switch (role)
            {
                case Role.PlatformAdmin: return "XFL administrator. Everything: all tickets, all users, master data, audit trail, system health.";
                case Role.SupportManager: return "XFL staff. Sees every ticket, assigns engineers, sets status, sees the ticket audit trail.";
                case Role.SupportEngineer: return "XFL staff. Sees every ticket, takes unassigned tickets, works on the tickets assigned to him.";
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

        /// <summary>XFL staff: platform administrator, support manager, support engineer.</summary>
        public static bool IsStaff(Role role)
        {
            return role == Role.PlatformAdmin || role == Role.SupportManager || role == Role.SupportEngineer;
        }

        /// <summary>
        /// The brokerage house an account belongs to for the purpose of the audit trail and of house administration:
        /// XFL staff accounts belong to the platform (null), everybody else to the house they registered for.
        /// </summary>
        public static int? HouseOf(User user)
        {
            return user.UType || user.UCatagory ? null : user.BrokerageHouseName;
        }
    }

    /// <summary>
    /// Put on an action of CsmsController (the base of the five role controllers): the action runs only for roles
    /// that hold one of the listed permissions. Everybody else gets the "does not exist" page.
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
