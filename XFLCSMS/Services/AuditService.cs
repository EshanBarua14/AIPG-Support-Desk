using XFLCSMS.Infrastructure;
using XFLCSMS.Models.Audit;
using XFLCSMS.Models.Register;

namespace XFLCSMS.Services
{
    /// <summary>The actions that are written to the audit trail. The part before the dot is the category.</summary>
    public static class AuditActions
    {
        public const string SignIn = "auth.signin";
        public const string SignInFailed = "auth.signin_failed";
        public const string SignInLocked = "auth.locked";
        public const string SignOut = "auth.signout";
        public const string Register = "auth.register";
        public const string Verify = "auth.verify";
        public const string PasswordResetRequest = "auth.reset_request";
        public const string PasswordReset = "auth.reset";
        public const string PasswordChange = "auth.password_change";

        public const string UserCreate = "user.create";
        public const string UserUpdate = "user.update";
        public const string UserActivate = "user.activate";
        public const string UserPasswordSet = "user.password_set";
        public const string UserUnlock = "user.unlock";
        public const string UserDelete = "user.delete";

        public const string TicketCreate = "ticket.create";
        public const string TicketEdit = "ticket.edit";
        public const string TicketAssign = "ticket.assign";
        public const string TicketUnassign = "ticket.unassign";
        public const string TicketStatus = "ticket.status";
        public const string TicketDelete = "ticket.delete";
        public const string TicketFileAdd = "ticket.file_add";
        public const string TicketFileDelete = "ticket.file_delete";
        public const string TicketReply = "ticket.reply";
        public const string TicketNote = "ticket.note";
        public const string TicketRate = "ticket.rate";

        public const string DataCreate = "data.create";
        public const string DataUpdate = "data.update";
        public const string DataDelete = "data.delete";

        public const string SystemMailTest = "system.mail_test";
        public const string SystemSmsTest = "system.sms_test";
        public const string SystemPermissions = "system.permissions";
        public const string SystemSettings = "system.settings";
        public const string SystemDemoData = "system.demo_data";
        public const string SystemStatusFix = "system.status_fix";

        /// <summary>Categories as offered in the filter of the audit page: key (prefix of the action) and name.</summary>
        public static readonly (string Key, string Name)[] Categories =
        {
            ("auth", "Sign-in and passwords"),
            ("user", "Accounts"),
            ("ticket", "Tickets"),
            ("data", "Master data"),
            ("system", "System")
        };

        /// <summary>Short name of an action for the list, e.g. "Ticket assigned".</summary>
        public static string Name(string action)
        {
            switch (action)
            {
                case SignIn: return "Signed in";
                case SignInFailed: return "Sign-in failed";
                case SignInLocked: return "Account locked";
                case SignOut: return "Signed out";
                case Register: return "Registered";
                case Verify: return "Account verified";
                case PasswordResetRequest: return "Reset token requested";
                case PasswordReset: return "Password reset";
                case PasswordChange: return "Password changed";
                case UserCreate: return "Account created";
                case UserUpdate: return "Account changed";
                case UserActivate: return "Account activated";
                case UserPasswordSet: return "Password set";
                case UserUnlock: return "Account unlocked";
                case UserDelete: return "Account deleted";
                case TicketCreate: return "Ticket raised";
                case TicketEdit: return "Ticket edited";
                case TicketReply: return "Reply";
                case TicketNote: return "Internal note";
                case TicketRate: return "Support rated";
                case TicketAssign: return "Ticket assigned";
                case TicketUnassign: return "Ticket unassigned";
                case TicketStatus: return "Status changed";
                case TicketDelete: return "Ticket deleted";
                case TicketFileAdd: return "File attached";
                case TicketFileDelete: return "File removed";
                case DataCreate: return "Created";
                case DataUpdate: return "Changed";
                case DataDelete: return "Deleted";
                case SystemMailTest: return "Mail server tested";
                case SystemSmsTest: return "SMS gateway tested";
                case SystemPermissions: return "Permissions changed";
                case SystemSettings: return "Settings changed";
                case SystemDemoData: return "Demo data";
                case SystemStatusFix: return "Statuses aligned";
                default: return action;
            }
        }

        /// <summary>How serious the line looks in the list: "bad" (failed, deleted), "good" (created, closed) or "".</summary>
        public static string Tone(string action)
        {
            switch (action)
            {
                case SignInFailed:
                case UserDelete:
                case TicketDelete:
                case DataDelete:
                case TicketFileDelete:
                    return "bad";
                case UserCreate:
                case UserActivate:
                case TicketCreate:
                case DataCreate:
                    return "good";
                default:
                    return string.Empty;
            }
        }
    }

    /// <summary>
    /// Adds lines to the audit trail. <see cref="Add"/> only attaches the line to the DataContext of the request,
    /// so it is stored in the same SaveChanges (and the same transaction) as the change it describes: either both
    /// are saved or neither. Use <see cref="SaveAsync"/> when there is no other change to save (sign-in, sign-out).
    /// </summary>
    public class AuditService
    {
        private readonly DataContext _context;
        private readonly IHttpContextAccessor _http;

        public AuditService(DataContext context, IHttpContextAccessor http)
        {
            _context = context;
            _http = http;
        }

        /// <param name="actor">The signed-in user, or null when nobody is signed in.</param>
        /// <param name="houseId">The brokerage house the line belongs to; null for the platform itself.</param>
        public AuditLog Add(string action, User? actor, string? actorRole, int? houseId, string? entityType, int? entityId, string? label, string? details, string? actorName = null)
        {
            var line = new AuditLog
            {
                At = DateTime.Now,
                UserId = actor?.Id,
                UserName = Cut(actor != null ? actor.FullName + " (" + actor.UserName + ")" : actorName ?? "(not signed in)", 200)!,
                Role = Cut(actorRole, 40),
                BrokerageId = houseId,
                Action = action,
                EntityType = Cut(entityType, 40),
                EntityId = entityId,
                EntityLabel = Cut(label, 200),
                Details = Cut(details, 4000),
                Ip = Cut(_http.HttpContext?.Connection.RemoteIpAddress?.ToString(), 64)
            };

            _context.AuditLogs.Add(line);
            return line;
        }

        /// <summary>
        /// Audit lines for master data that is about to be saved: brokerage houses, branches, products and the four support lists.
        /// Looks at what the DataContext is tracking, so no page that changes such a row can forget the line.
        /// </summary>
        public void AddMasterDataChanges(User? actor, string? actorRole)
        {
            foreach (var entry in _context.ChangeTracker.Entries().ToList())
            {
                if (entry.State != EntityState.Added && entry.State != EntityState.Modified && entry.State != EntityState.Deleted)
                {
                    continue;
                }

                string type, nameProperty, idProperty;
                int? houseId = null;
                switch (entry.Entity)
                {
                    case Models.Brocarage.Brokerage house:
                        type = "Brokerage house"; nameProperty = nameof(house.BrokerageHouseName); idProperty = nameof(house.BrokerageId);
                        houseId = entry.State == EntityState.Added ? null : house.BrokerageId;
                        break;
                    case Models.Branch.Branchh branch:
                        type = "Branch"; nameProperty = nameof(branch.BranchName); idProperty = nameof(branch.BranchId);
                        houseId = branch.BrokerageId;
                        break;
                    case Models.Support.Product:
                        type = "Product"; nameProperty = "Name"; idProperty = "ProductId";
                        break;
                    case Models.Support.SupportType:
                        type = "Support type"; nameProperty = "SType"; idProperty = "SupportTypeId";
                        break;
                    case Models.Support.SupportCatagory:
                        type = "Support category"; nameProperty = "SCatagory"; idProperty = "SupportCatagoryId";
                        break;
                    case Models.Support.SupportSubCatagory:
                        type = "Support sub-category"; nameProperty = "SubCatagory"; idProperty = "SupportSubCatagoryId";
                        break;
                    case Models.Affected.AffectedSection:
                        type = "Affected section"; nameProperty = "ASection"; idProperty = "AffectedSectionId";
                        break;
                    default:
                        continue;
                }

                var name = entry.Property(nameProperty).CurrentValue as string;
                var id = entry.State == EntityState.Added ? (int?)null : entry.Property(idProperty).CurrentValue as int?;

                if (entry.State == EntityState.Added)
                {
                    Add(AuditActions.DataCreate, actor, actorRole, houseId, type, id, name, "Created the " + type.ToLowerInvariant() + " \u201c" + name + "\u201d");
                }
                else if (entry.State == EntityState.Deleted)
                {
                    name = entry.Property(nameProperty).OriginalValue as string ?? name;
                    Add(AuditActions.DataDelete, actor, actorRole, houseId, type, id, name, "Deleted the " + type.ToLowerInvariant() + " \u201c" + name + "\u201d");
                }
                else if (entry.Entity is Models.Support.Product)
                {
                    // a product has several fields: say which one changed
                    var parts = new List<string>();
                    foreach (var property in entry.Properties.Where(p => p.IsModified && !Equals(p.OriginalValue, p.CurrentValue)))
                    {
                        switch (property.Metadata.Name)
                        {
                            case "Name": parts.Add("name from \u201c" + property.OriginalValue + "\u201d to \u201c" + property.CurrentValue + "\u201d"); break;
                            case "Code": parts.Add("short name from \u201c" + property.OriginalValue + "\u201d to \u201c" + property.CurrentValue + "\u201d"); break;
                            case "Description": parts.Add("description changed"); break;
                            case "IsActive": parts.Add(Equals(property.CurrentValue, true) ? "made active" : "made inactive"); break;
                        }
                    }

                    if (parts.Count > 0)
                    {
                        Add(AuditActions.DataUpdate, actor, actorRole, houseId, type, id, name, "Changed the product \u201c" + name + "\u201d: " + string.Join(", ", parts));
                    }
                }
                else
                {
                    var changes = entry.Properties
                        .Where(p => p.IsModified && !Equals(p.OriginalValue, p.CurrentValue) && p.Metadata.ClrType == typeof(string))
                        .Select(p => "\u201c" + p.OriginalValue + "\u201d to \u201c" + p.CurrentValue + "\u201d")
                        .ToList();
                    var text = changes.Count > 0
                        ? "Changed the " + type.ToLowerInvariant() + " from " + string.Join(", ", changes)
                        : "Changed the " + type.ToLowerInvariant() + " \u201c" + name + "\u201d";

                    // what is not text: the product a support list entry belongs to
                    var more = new List<string>();
                    foreach (var property in entry.Properties.Where(p => p.IsModified && !Equals(p.OriginalValue, p.CurrentValue)))
                    {
                        if (property.Metadata.Name == "ProductId")
                        {
                            more.Add("product from \u201c" + ProductName(property.OriginalValue as int?) + "\u201d to \u201c" + ProductName(property.CurrentValue as int?) + "\u201d");
                        }
                    }

                    if (changes.Count > 0 || more.Count > 0)
                    {
                        Add(AuditActions.DataUpdate, actor, actorRole, houseId, type, id, name, text + (more.Count > 0 ? (changes.Count > 0 ? "; " : ": ") + string.Join(", ", more) : string.Empty));
                    }
                }
            }
        }

        private string ProductName(int? id)
        {
            return id == null ? "All products" : _context.Products.Where(product => product.ProductId == id).Select(product => product.Name).FirstOrDefault() ?? "(deleted product)";
        }

        public Task SaveAsync()
        {
            return _context.SaveChangesAsync();
        }

        /// <summary>The history of one item, oldest first (shown on the ticket page).</summary>
        public Task<List<AuditLog>> HistoryAsync(string entityType, int entityId)
        {
            return _context.AuditLogs
                .Where(line => line.EntityType == entityType && line.EntityId == entityId)
                .OrderBy(line => line.Id)
                .ToListAsync();
        }

        private static string? Cut(string? text, int length)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            return text.Length <= length ? text : text.Substring(0, length - 1) + "…";
        }
    }
}
