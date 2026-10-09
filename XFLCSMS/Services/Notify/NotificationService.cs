using System.Globalization;
using System.Net;
using XFLCSMS.Infrastructure;
using XFLCSMS.Models.Issue;
using XFLCSMS.Models.Notify;
using XFLCSMS.Models.Register;

namespace XFLCSMS.Services.Notify
{
    /// <summary>
    /// Tells people what happened. <see cref="Tell"/> only adds rows to the DataContext of the request: the
    /// notification for the bell, and one row per e-mail / SMS to send. They are saved in the same transaction as
    /// the change they report. After that save the toasts go out to the open pages (NotificationHub) and the
    /// worker that sends e-mail and SMS is woken (NotificationWorker).
    /// Which event uses which channel is a setting; each user can switch channels off for himself.
    /// </summary>
    public class NotificationService
    {
        public const string SiteUrlKey = "site.url";

        private readonly DataContext _db;
        private readonly SettingsStore _settings;
        private readonly NotificationHub _hub;
        private readonly NotificationWorker _worker;
        private readonly List<Notification> _fresh = new();
        private bool _queued;

        public NotificationService(DataContext db, SettingsStore settings, NotificationHub hub, NotificationWorker worker)
        {
            _db = db;
            _settings = settings;
            _hub = hub;
            _worker = worker;
            _db.AfterSaving = Flush;
        }

        /// <summary>After a successful save: show the toasts, wake the sender.</summary>
        private void Flush()
        {
            foreach (var notice in _fresh.Where(item => item.Id > 0).ToList())
            {
                _hub.Publish(notice.UserId, ToLive(notice));
                _fresh.Remove(notice);
            }

            if (_queued)
            {
                _queued = false;
                _worker.Wake();
            }
        }

        public static LiveNotice ToLive(Notification notice)
        {
            return new LiveNotice
            {
                Id = notice.Id,
                Kind = notice.Kind,
                Title = notice.Title,
                Body = notice.Body,
                HasLink = !string.IsNullOrEmpty(notice.LinkAction),
                At = notice.At.ToString("dd MMM yyyy, h:mm tt", CultureInfo.InvariantCulture)
            };
        }

        /// <summary>
        /// Tells the given users. The person who did it (<paramref name="actorId"/>) is never told, nor is an
        /// account that cannot sign in. The rows are saved with the next SaveChanges of the request.
        /// </summary>
        /// <param name="linkAction">Page to open in the area of the reader, e.g. "TicketView" (with <paramref name="linkId"/>).</param>
        public void Tell(string kind, IEnumerable<int?> recipients, string title, string? body, string? linkAction, int? linkId, int? actorId)
        {
            var ids = recipients.Where(id => id != null && id != actorId).Select(id => id!.Value).Distinct().ToList();
            if (ids.Count == 0)
            {
                return;
            }

            var inApp = On(kind, NotificationEvents.InAppChannel);
            var email = On(kind, NotificationEvents.EmailChannel);
            var sms = On(kind, NotificationEvents.SmsChannel);
            if (!inApp && !email && !sms)
            {
                return;
            }

            // whole entities on purpose: a user changed in this request (just activated) is seen as he is now
            var users = _db.Users.Where(user => ids.Contains(user.Id)).ToList().Where(user => user.UStatus && user.VerifiedAt != null).ToList();
            var wishes = _db.NotificationPreferences.Where(wish => ids.Contains(wish.UserId)).ToList();
            var now = DateTime.Now;
            title = Cut(title, 200)!;
            body = Cut(body, 1000);

            foreach (var user in users)
            {
                var wish = wishes.FirstOrDefault(item => item.UserId == user.Id);
                if (inApp && (wish?.InApp ?? true))
                {
                    var notice = new Notification { UserId = user.Id, At = now, Kind = kind, Title = title, Body = body, LinkAction = linkAction, LinkId = linkId };
                    _db.Notifications.Add(notice);
                    _fresh.Add(notice);
                }

                if (email && (wish?.Email ?? true) && !string.IsNullOrWhiteSpace(user.Email))
                {
                    _db.NotificationDeliveries.Add(new NotificationDelivery
                    {
                        UserId = user.Id, Kind = kind, Channel = NotificationDelivery.Email, Recipient = Cut(user.Email.Trim(), 200)!,
                        Subject = Cut("AIPG Support Desk: " + title, 200), Body = MailBody(user, title, body, linkAction, linkId), CreatedAt = now
                    });
                    _queued = true;
                }

                if (sms && (wish?.Sms ?? true) && !string.IsNullOrWhiteSpace(user.PhonNumber))
                {
                    _db.NotificationDeliveries.Add(new NotificationDelivery
                    {
                        UserId = user.Id, Kind = kind, Channel = NotificationDelivery.Sms, Recipient = Cut(user.PhonNumber.Trim(), 200)!,
                        Body = SmsBody(title, body), CreatedAt = now
                    });
                    _queued = true;
                }
            }
        }

        private bool On(string kind, string channel)
        {
            return NotificationEvents.ChannelOn(_settings, channel) && NotificationEvents.EventOn(_settings, kind, channel);
        }

        // ---- who ---------------------------------------------------------------------------------

        /// <summary>AIPG staff whose role holds the permission.</summary>
        public List<int?> StaffWith(Permission permission)
        {
            return _db.Users.Where(user => (user.UType || user.UCatagory) && user.UStatus).ToList()
                .Where(user => Rbac.IsStaff(Rbac.RoleOf(user)) && Rbac.Can(Rbac.RoleOf(user), permission))
                .Select(user => (int?)user.Id).ToList();
        }

        /// <summary>People of a brokerage house whose role holds the permission.</summary>
        public List<int?> HousePeopleWith(Permission permission, int houseId)
        {
            return _db.Users.Where(user => user.BrokerageHouseName == houseId && !user.UType && !user.UCatagory && user.UStatus).ToList()
                .Where(user => Rbac.Can(Rbac.RoleOf(user), permission))
                .Select(user => (int?)user.Id).ToList();
        }

        /// <summary>
        /// The people who watch the ticket and may (still) open it: AIPG staff, the person who raised it, and people
        /// of its brokerage house who see the tickets of the house. Internal things go to staff only.
        /// </summary>
        public List<int?> Watchers(IssueTable issue, bool staffOnly = false)
        {
            var ids = _db.TicketWatchers.Where(row => row.IssueId == issue.IssueId).Select(row => row.UserId).ToList();
            if (ids.Count == 0)
            {
                return new List<int?>();
            }

            return _db.Users.Where(user => ids.Contains(user.Id)).ToList()
                .Where(user =>
                {
                    var role = Rbac.RoleOf(user);
                    if (Rbac.IsStaff(role))
                    {
                        return Rbac.Can(role, Permission.TicketsAll) || user.Id == issue.UserId || TicketService.IsAssignedTo(issue, user);
                    }

                    return !staffOnly && (user.Id == issue.UserId || (user.BrokerageHouseName == issue.BrokerageId && Rbac.Can(role, Permission.TicketsHouse)));
                })
                .Select(user => (int?)user.Id).ToList();
        }

        // ---- tickets -----------------------------------------------------------------------------

        public void TicketRaised(IssueTable issue, User raiser)
        {
            var house = _db.Brokerages.Where(b => b.BrokerageId == issue.BrokerageId).Select(b => b.BrokerageHouseName).FirstOrDefault();
            Tell(NotificationEvents.TicketCreated,
                StaffWith(Permission.TicketAssign).Concat(HousePeopleWith(Permission.UsersHouse, issue.BrokerageId)),
                "New ticket " + issue.TNumber + ": " + issue.ITitle,
                "Raised by " + raiser.FullName + (house == null ? string.Empty : " (" + house + ")") + ", priority " + issue.Priority + ".",
                "TicketView", issue.IssueId, raiser.Id);
        }

        /// <param name="previousEngineerId">The engineer who had the ticket before, if any.</param>
        public void TicketAssigned(IssueTable issue, User engineer, int? previousEngineerId, User? actor)
        {
            var by = actor == null || actor.Id == engineer.Id ? string.Empty : " by " + actor.FullName;
            Tell(NotificationEvents.TicketAssigned, new int?[] { engineer.Id },
                "Ticket " + issue.TNumber + " is assigned to you", "“" + issue.ITitle + "”, priority " + issue.Priority + by + ".",
                "TicketView", issue.IssueId, actor?.Id);
            if (issue.UserId != engineer.Id)
            {
                Tell(NotificationEvents.TicketAssigned, new int?[] { issue.UserId }.Concat(Watchers(issue)).Where(id => id != engineer.Id),
                    "Ticket " + issue.TNumber + " is with " + engineer.FullName + " now", "“" + issue.ITitle + "” has a support engineer.",
                    "TicketView", issue.IssueId, actor?.Id);
            }

            if (previousEngineerId != null && previousEngineerId != engineer.Id)
            {
                Tell(NotificationEvents.TicketUnassigned, new[] { previousEngineerId },
                    "Ticket " + issue.TNumber + " went to " + engineer.FullName, "“" + issue.ITitle + "” is no longer assigned to you.",
                    "TicketView", issue.IssueId, actor?.Id);
            }
        }

        public void TicketUnassigned(IssueTable issue, int? previousEngineerId, string? previousEngineerName, User? actor)
        {
            if (previousEngineerId != null)
            {
                Tell(NotificationEvents.TicketUnassigned, new[] { previousEngineerId },
                    "Ticket " + issue.TNumber + " is no longer assigned to you", "“" + issue.ITitle + "” was unassigned" + By(actor) + ".",
                    "TicketView", issue.IssueId, actor?.Id);
            }

            Tell(NotificationEvents.TicketUnassigned, StaffWith(Permission.TicketAssign).Where(id => id != previousEngineerId),
                "Ticket " + issue.TNumber + " needs an engineer again",
                "“" + issue.ITitle + "”" + (previousEngineerName == null ? " is unassigned." : " was given back by " + previousEngineerName + "."),
                "TicketView", issue.IssueId, actor?.Id);
        }

        public void TicketStatusChanged(IssueTable issue, string oldStatus, User? actor)
        {
            var finished = TicketStatus.NeedsClosePermission(issue.IStatus);
            Tell(finished ? NotificationEvents.TicketClosed : NotificationEvents.TicketStatus,
                new[] { issue.UserId, issue.AssignedToId }.Concat(Watchers(issue)),
                "Ticket " + issue.TNumber + ": " + TicketStatus.Name(issue.IStatus),
                "“" + issue.ITitle + "” went from " + TicketStatus.Name(oldStatus) + " to " + TicketStatus.Name(issue.IStatus) + By(actor) + ".",
                "TicketView", issue.IssueId, actor?.Id);
        }

        public void TicketEdited(IssueTable issue, string what, User? actor)
        {
            Tell(NotificationEvents.TicketEdited, new[] { issue.UserId, issue.AssignedToId }.Concat(Watchers(issue)),
                "Ticket " + issue.TNumber + " was updated",
                (actor == null ? "Somebody" : actor.FullName) + " " + char.ToLowerInvariant(what[0]) + what.Substring(1) + " on “" + issue.ITitle + "”.",
                "TicketView", issue.IssueId, actor?.Id);
        }

        /// <summary>A reply in the conversation of a ticket, readable by the house.</summary>
        public void TicketReplied(IssueTable issue, User author, bool fromStaff, string preview)
        {
            var who = new List<int?> { issue.UserId, issue.AssignedToId };
            if (issue.AssignedToId == null)
            {
                // nobody works on it yet: the people who hand out tickets should see that the house wrote
                who.AddRange(StaffWith(Permission.TicketAssign));
            }

            who.AddRange(Watchers(issue));
            Tell(NotificationEvents.TicketReply, who,
                (fromStaff ? "AIPG replied on ticket " : "New reply on ticket ") + issue.TNumber,
                author.FullName + ": " + preview, "TicketView", issue.IssueId, author.Id);
        }

        /// <summary>An internal note: only ever goes to AIPG staff.</summary>
        public void TicketNoted(IssueTable issue, User author, string preview)
        {
            var who = issue.AssignedToId != null ? new List<int?> { issue.AssignedToId } : StaffWith(Permission.TicketAssign);
            var staff = _db.Users.Where(user => user.UType || user.UCatagory).Select(user => (int?)user.Id).ToList();
            who.AddRange(Watchers(issue, staffOnly: true));
            Tell(NotificationEvents.TicketNote, who.Where(id => staff.Contains(id)),
                "Internal note on ticket " + issue.TNumber, author.FullName + ": " + preview, "TicketView", issue.IssueId, author.Id);
        }

        public void TicketRated(IssueTable issue, User rater)
        {
            Tell(NotificationEvents.TicketRated, new List<int?> { issue.AssignedToId }.Concat(StaffWith(Permission.TicketAssign)),
                "Ticket " + issue.TNumber + " was rated " + issue.Rating + " of 5",
                rater.FullName + (string.IsNullOrWhiteSpace(issue.RatingComment) ? " rated the support." : ": " + issue.RatingComment),
                "TicketView", issue.IssueId, rater.Id);
        }

        /// <summary>The ticket has been "Pending" for a while: the person who raised it is asked to look at it.</summary>
        public void TicketReminder(IssueTable issue, int days)
        {
            Tell(NotificationEvents.TicketReminder, new int?[] { issue.UserId }.Concat(Watchers(issue).Where(id => id != issue.AssignedToId)),
                "Ticket " + issue.TNumber + " is waiting for an answer",
                "\u201c" + issue.ITitle + "\u201d has been pending for " + (days == 1 ? "1 day" : days + " days") + ". Please open it and reply, so the work can go on.",
                "TicketView", issue.IssueId, null);
        }

        /// <summary>A service target of the ticket comes up (<paramref name="missed"/> false) or has passed (true).</summary>
        public void SlaAlert(IssueTable issue, bool response, bool missed, string when)
        {
            var what = response ? "first response" : "solution";
            var who = new List<int?> { issue.AssignedToId };
            if (missed || issue.AssignedToId == null)
            {
                who.AddRange(StaffWith(Permission.TicketAssign));
            }

            Tell(missed ? NotificationEvents.SlaMissed : NotificationEvents.SlaSoon, who,
                "Ticket " + issue.TNumber + ": " + what + (missed ? " is overdue" : " is due soon"),
                "\u201c" + issue.ITitle + "\u201d, priority " + issue.Priority + ". The " + what + " " + when + ".",
                "TicketView", issue.IssueId, null);
        }

        private static string By(User? actor, string lead = " by ")
        {
            return actor == null ? string.Empty : lead + actor.FullName;
        }

        // ---- accounts ----------------------------------------------------------------------------

        public void AccountWaiting(User account)
        {
            var house = Rbac.HouseOf(account);
            var who = StaffWith(Permission.UsersAll);
            if (house != null) { who = who.Concat(HousePeopleWith(Permission.UsersHouse, house.Value)).ToList(); }
            Tell(NotificationEvents.AccountWaiting, who,
                account.FullName + " registered", "The account " + account.UserName + " waits for the token from the e-mail. You can activate it yourself.",
                "EditUser", account.Id, account.Id);
        }

        public void AccountActivated(User account, User? actor)
        {
            Tell(NotificationEvents.AccountActivated, new int?[] { account.Id },
                "Your account is active", "You can sign in to AIPG Support Desk now with the user name " + account.UserName + ".",
                null, null, actor?.Id);
        }

        public void AccountCreated(User account, User? actor)
        {
            Tell(NotificationEvents.AccountCreated, new int?[] { account.Id },
                "An account was created for you",
                "Your user name for AIPG Support Desk is " + account.UserName + ". You get the password from " + (actor?.FullName ?? "your administrator") + "; at the first sign-in you choose your own.",
                null, null, actor?.Id);
        }

        public void AccountPasswordSet(User account, User? actor)
        {
            Tell(NotificationEvents.AccountPassword, new int?[] { account.Id },
                "Your password was changed by an administrator",
                (actor?.FullName ?? "An administrator") + " set a new password for " + account.UserName + ". When you sign in with it you choose your own. If you did not ask for this, contact AIPG.",
                null, null, actor?.Id);
        }

        // ---- texts -------------------------------------------------------------------------------

        /// <summary>
        /// Address of the site for links in e-mails: the one entered on the settings page, or null (then e-mails carry
        /// no link). Never the address of the current request: that comes from the browser of whoever caused the
        /// message - also somebody who is not signed in - and would put any host name he likes into mail to others.
        /// </summary>
        public string? SiteUrl => _settings.Get(SiteUrlKey)?.TrimEnd('/');

        public string MailBody(User reader, string title, string? body, string? linkAction, int? linkId)
        {
            var site = SiteUrl;
            var link = site == null ? null
                : linkAction == null ? site + "/"
                : site + "/" + Rbac.Controller(Rbac.RoleOf(reader)) + "/" + linkAction + (linkId == null ? string.Empty : "/" + linkId.Value.ToString(CultureInfo.InvariantCulture));

            var html = "<div style=\"font-family:Segoe UI,Arial,sans-serif;font-size:14px;line-height:1.5;color:#1f2937;max-width:560px\">"
                + "<p style=\"margin:0 0 4px;color:#6b7280;font-size:12px\">AIPG Support Desk</p>"
                + "<p style=\"margin:0 0 8px;font-size:17px;font-weight:600\">" + WebUtility.HtmlEncode(title) + "</p>";
            if (!string.IsNullOrEmpty(body))
            {
                html += "<p style=\"margin:0 0 16px\">" + WebUtility.HtmlEncode(body) + "</p>";
            }

            if (link != null)
            {
                html += "<p style=\"margin:0 0 20px\"><a href=\"" + WebUtility.HtmlEncode(link) + "\" style=\"display:inline-block;background:#1d4ed8;color:#ffffff;text-decoration:none;padding:8px 14px;border-radius:6px\">"
                    + (linkAction == null ? "Sign in" : "Open in AIPG Support Desk") + "</a></p>";
            }

            return html + "<p style=\"margin:0;color:#6b7280;font-size:12px\">Hello " + WebUtility.HtmlEncode(reader.FullName)
                + ", you get this message because of the notification settings of AIPG Support Desk. You can switch e-mail off for yourself under the bell &gt; Preferences.</p></div>";
        }

        public static string SmsBody(string title, string? body)
        {
            var text = "AIPG Support Desk: " + title + (string.IsNullOrEmpty(body) ? string.Empty : ". " + body);
            return text.Length <= 300 ? text : text.Substring(0, 299) + "…";
        }

        private static string? Cut(string? text, int length)
        {
            return string.IsNullOrEmpty(text) || text.Length <= length ? text : text.Substring(0, length - 1) + "…";
        }
    }
}
