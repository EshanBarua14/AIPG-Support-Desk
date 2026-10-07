namespace XFLCSMS.Services
{
    /// <summary>
    /// The statuses of a ticket, in the order of its life. This is the one definition: lists, filters, the board,
    /// reports, colours and the rules in TicketService all read it.
    ///
    ///   Unassigned ─ assign ─► Assigned ─► In progress ─► Waiting for review ─► Done ─► Deployed ─► Closed
    ///                                          ▲  │
    ///                                          └─ Pending (waiting for somebody else)
    ///
    /// The stored values of the first three and of the last one are the ones the system always had
    /// ("Open", "Inqueue", "Inprogress", "Close"), so existing tickets keep working.
    /// </summary>
    public static class TicketStatus
    {
        public const string Unassigned = "Open";
        public const string Assigned = "Inqueue";
        public const string InProgress = "Inprogress";
        public const string Pending = "Pending";
        public const string Review = "Review";
        public const string Done = "Done";
        public const string Deployed = "Deployed";
        public const string Closed = "Close";

        public sealed class Info
        {
            public Info(string key, string name, string css, string icon, string about)
            {
                Key = key; Name = name; Css = css; Icon = icon; About = about;
            }

            /// <summary>The value stored in Issues.IStatus.</summary>
            public string Key { get; }
            /// <summary>What people read.</summary>
            public string Name { get; }
            /// <summary>Part of the css class ("st-review").</summary>
            public string Css { get; }
            public string Icon { get; }
            public string About { get; }
        }

        public static readonly Info[] All =
        {
            new(Unassigned, "Unassigned", "open", "inbox", "Raised, no support engineer yet."),
            new(Assigned, "Assigned", "inqueue", "user-check", "An engineer has the ticket and has not started."),
            new(InProgress, "In progress", "inprogress", "loader", "The engineer is working on it."),
            new(Pending, "Pending", "pending", "hourglass", "Work has stopped: waiting for information or for somebody else."),
            new(Review, "Waiting for review", "review", "eye", "The work is finished and waits to be checked."),
            new(Done, "Done", "done", "check", "Checked and accepted; not yet in production."),
            new(Deployed, "Deployed", "deployed", "rocket", "The fix or change is live."),
            new(Closed, "Closed", "close", "circle-check-big", "Nothing more to do.")
        };

        /// <summary>The statuses in which an engineer is working on the ticket: they need an engineer.</summary>
        public static readonly string[] Work = { Assigned, InProgress, Pending, Review, Done, Deployed };

        public static readonly string[] Keys = All.Select(status => status.Key).ToArray();

        public static Info? Find(string? status)
        {
            var key = Normalize(status);
            return key == null ? null : All.First(item => item.Key == key);
        }

        public static string Name(string? status)
        {
            return Find(status)?.Name ?? (string.IsNullOrWhiteSpace(status) ? "No status" : status);
        }

        public static bool IsClosed(string? status)
        {
            return Normalize(status) == Closed;
        }

        /// <summary>Setting this status (or leaving it by reopening) needs the permission "deploy, close and reopen".</summary>
        public static bool NeedsClosePermission(string? status)
        {
            var key = Normalize(status);
            return key == Deployed || key == Closed;
        }

        /// <summary>
        /// The stored key for a status however it was written ("In Progress", "closed", "waiting for review", ...),
        /// or null for something that is no status.
        /// </summary>
        public static string? Normalize(string? status)
        {
            switch ((status ?? string.Empty).Replace(" ", string.Empty).Replace("-", string.Empty).Replace("_", string.Empty).ToLowerInvariant())
            {
                case "open":
                case "unassigned":
                case "new":
                    return Unassigned;
                case "inqueue":
                case "assigned":
                case "queue":
                    return Assigned;
                case "inprogress":
                case "progress":
                case "working":
                    return InProgress;
                case "pending":
                case "onhold":
                case "hold":
                    return Pending;
                case "review":
                case "waitingforreview":
                case "inreview":
                    return Review;
                case "done":
                case "resolved":
                case "completed":
                    return Done;
                case "deployed":
                case "released":
                    return Deployed;
                case "close":
                case "closed":
                    return Closed;
                default:
                    return null;
            }
        }
    }
}
