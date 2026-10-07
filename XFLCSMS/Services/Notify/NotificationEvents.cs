namespace XFLCSMS.Services.Notify
{
    /// <summary>
    /// The events people are told about, who is told, and through which channels when nobody has changed the
    /// settings. The settings page shows this list as a table of switches (stored as "notify.event.KEY.CHANNEL").
    /// </summary>
    public static class NotificationEvents
    {
        public const string TicketCreated = "ticket.created";
        public const string TicketAssigned = "ticket.assigned";
        public const string TicketUnassigned = "ticket.unassigned";
        public const string TicketStatus = "ticket.status";
        public const string TicketClosed = "ticket.closed";
        public const string TicketEdited = "ticket.edited";
        public const string AccountWaiting = "account.waiting";
        public const string AccountActivated = "account.activated";
        public const string AccountCreated = "account.created";
        public const string AccountPassword = "account.password";
        public const string Test = "system.test";

        public sealed class Info
        {
            public Info(string key, string group, string name, string audience, bool inApp, bool email, bool sms)
            {
                Key = key; Group = group; Name = name; Audience = audience; InApp = inApp; Email = email; Sms = sms;
            }

            public string Key { get; }
            public string Group { get; }
            public string Name { get; }
            /// <summary>Who is told (never the person who did it).</summary>
            public string Audience { get; }
            public bool InApp { get; }
            public bool Email { get; }
            public bool Sms { get; }
        }

        public static readonly Info[] All =
        {
            new(TicketCreated, "Tickets", "Ticket raised", "XFL staff who assign tickets, and the administrators of the house it was raised for", true, true, false),
            new(TicketAssigned, "Tickets", "Ticket assigned", "The engineer who got it, and the person who raised it", true, true, true),
            new(TicketUnassigned, "Tickets", "Ticket unassigned", "The engineer who had it, and XFL staff who assign tickets", true, false, false),
            new(TicketStatus, "Tickets", "Status changed", "The person who raised it and its engineer", true, false, false),
            new(TicketClosed, "Tickets", "Ticket deployed or closed", "The person who raised it and its engineer", true, true, true),
            new(TicketEdited, "Tickets", "Ticket edited or commented", "The person who raised it and its engineer", true, false, false),
            // no e-mail by default: registering needs no sign-in, and every registration would mail every administrator
            new(AccountWaiting, "Accounts", "Registration waiting for activation", "The administrators who can activate the account", true, false, false),
            new(AccountActivated, "Accounts", "Account activated", "The owner of the account", false, true, true),
            new(AccountCreated, "Accounts", "Account created by an administrator", "The owner of the account", false, true, false),
            new(AccountPassword, "Accounts", "Password set by an administrator", "The owner of the account", false, true, true)
        };

        public static Info? Find(string key)
        {
            return All.FirstOrDefault(item => item.Key == key);
        }

        public const string InAppChannel = "inapp";
        public const string EmailChannel = "email";
        public const string SmsChannel = "sms";

        /// <summary>Master switch of a channel: "notify.inapp", "notify.email", "notify.sms".</summary>
        public static string ChannelKey(string channel) => "notify." + channel;

        public static string EventKey(string eventKey, string channel) => "notify.event." + eventKey + "." + channel;

        public static bool ChannelOn(SettingsStore settings, string channel)
        {
            // toasts and e-mail work out of the box; SMS needs a gateway first
            return settings.GetBool(ChannelKey(channel), channel != SmsChannel);
        }

        public static bool EventOn(SettingsStore settings, string eventKey, string channel)
        {
            var info = Find(eventKey);
            var fallback = info == null || (channel == InAppChannel ? info.InApp : channel == EmailChannel ? info.Email : info.Sms);
            return settings.GetBool(EventKey(eventKey, channel), fallback);
        }
    }
}
