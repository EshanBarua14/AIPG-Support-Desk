using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using XFLCSMS.Models.Register;
using XFLCSMS.Services;

namespace XFLCSMS.Infrastructure
{
    /// <summary>
    /// Small presentation helpers shared by the views: who is looking at the page (role), and how
    /// dates, ticket numbers, statuses and priorities are shown. Keeping them here means a ticket
    /// looks the same on every page.
    /// </summary>
    public static class Ui
    {
        /// <summary>The role a page belongs to. Each role has its own controller, so the controller name decides.</summary>
        public sealed class RoleInfo
        {
            public string Controller { get; init; } = "Admin";
            public string Label { get; init; } = "Administrator";
            public bool IsAdmin { get; init; }
            public bool IsMaker { get; init; }
            public bool IsEngineer { get; init; }
            /// <summary>XFL staff: admin, support manager, support engineer.</summary>
            public bool IsStaff => !IsMaker;
            /// <summary>Action name of the role's main ticket list.</summary>
            public string TicketList { get; init; } = "AdminView";
            public bool CanCreateTicket => !IsAdmin;
        }

        public static RoleInfo Role(ViewContext context)
        {
            return Role(context.RouteData.Values["controller"]?.ToString());
        }

        public static RoleInfo Role(string? controller)
        {
            switch (controller)
            {
                case "Maker":
                    return new RoleInfo { Controller = "Maker", Label = "Maker", IsMaker = true, TicketList = "MackerTicketList" };
                case "SupportEngineer":
                    return new RoleInfo { Controller = "SupportEngineer", Label = "Support Engineer", IsEngineer = true, TicketList = "AllTicketList" };
                case "SupportManegar":
                    return new RoleInfo { Controller = "SupportManegar", Label = "Support Manager", TicketList = "AllTicketList" };
                default:
                    return new RoleInfo { Controller = "Admin", Label = "Administrator", IsAdmin = true, TicketList = "AdminView" };
            }
        }

        /// <summary>The role a user gets when signing in (same order as RegisterLoginController.Login).</summary>
        public static string RoleOf(User user)
        {
            return RoleOf(user.UCatagory, user.UType, user.Department);
        }

        public static string RoleOf(bool isAdmin, bool isXflStaff, string? department)
        {
            if (isAdmin) { return "Administrator"; }
            if (isXflStaff && department == "Support Maneger") { return "Support Manager"; }
            if (isXflStaff && department == "Support Engineer") { return "Support Engineer"; }
            return "Maker";
        }

        public static IHtmlContent RolePill(string roleName)
        {
            return Pill((roleName == "Administrator" ? "role-admin" : roleName == "Maker" ? string.Empty : "role-staff") + " plain", roleName);
        }

        public static string Date(DateTime? value)
        {
            return value.HasValue ? value.Value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) : string.Empty;
        }

        /// <summary>Date and time, e.g. "07 Oct 2026, 10:53 AM".</summary>
        public static string Stamp(DateTime? value)
        {
            return value.HasValue ? value.Value.ToString("dd MMM yyyy, h:mm tt", CultureInfo.InvariantCulture) : string.Empty;
        }

        public static string Initials(string? name)
        {
            var parts = (name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) { return "?"; }
            var first = parts[0].Substring(0, 1);
            var last = parts.Length > 1 ? parts[^1].Substring(0, 1) : string.Empty;
            return (first + last).ToUpperInvariant();
        }

        public static int Percent(int? part, int? whole)
        {
            if (!part.HasValue || !whole.HasValue || whole.Value <= 0) { return 0; }
            return (int)Math.Round(part.Value * 100.0 / whole.Value);
        }

        /// <summary>"ABC_0000042" as a two-part chip: house acronym + running number.</summary>
        public static IHtmlContent Sym(string? ticketNumber, bool large = false)
        {
            var number = ticketNumber ?? string.Empty;
            var cut = number.LastIndexOf('_');
            var house = cut > 0 ? number.Substring(0, cut) : string.Empty;
            var serial = cut > 0 ? number.Substring(cut + 1) : number;

            var html = "<span class=\"sym" + (large ? " lg" : string.Empty) + "\" title=\"Ticket " + Encode(number) + "\">";
            if (house.Length > 0) { html += "<b>" + Encode(house) + "</b>"; }
            html += "<i>" + Encode(serial) + "</i></span>";
            return new HtmlString(html);
        }

        public static IHtmlContent StatusPill(string? status)
        {
            return Pill(StatusClass(status), string.IsNullOrEmpty(status) ? "No status" : DisplayText.Status(status));
        }

        public static string StatusClass(string? status)
        {
            var key = StatusKey(status);
            return key.Length == 0 ? string.Empty : "st-" + key;
        }

        /// <summary>
        /// "open" / "inqueue" / "inprogress" / "close", or "" for anything else. Tolerates the spellings older
        /// versions stored ("In Progress", "Closed", ...), so old tickets get the right colour and progress step.
        /// </summary>
        public static string StatusKey(string? status)
        {
            switch ((status ?? string.Empty).Replace(" ", string.Empty).Replace("-", string.Empty).ToLowerInvariant())
            {
                case "open": return "open";
                case "inqueue": return "inqueue";
                case "inprogress": return "inprogress";
                case "close":
                case "closed": return "close";
                default: return string.Empty;
            }
        }

        public static IHtmlContent PriorityPill(string? priority)
        {
            return string.IsNullOrEmpty(priority) ? HtmlString.Empty : Pill("pr-" + PriorityKey(priority), priority);
        }

        /// <summary>"high" / "medium" / "low" (or "none"), for css classes.</summary>
        public static string PriorityKey(string? priority)
        {
            switch (priority)
            {
                case "High": return "high";
                case "Medium": return "medium";
                case "Low": return "low";
                default: return "none";
            }
        }

        public static IHtmlContent TodoPill(string? status)
        {
            switch (status)
            {
                case "Done": return Pill("st-done", "Done");
                case "Canceled": return Pill("st-canceled", "Canceled");
                case "In progress": return Pill("st-inprogress", "In progress");
                default: return Pill(string.Empty, string.IsNullOrEmpty(status) ? "No status" : status);
            }
        }

        public static IHtmlContent Pill(string cssClass, string text)
        {
            return new HtmlString("<span class=\"pill " + Encode(cssClass) + "\">" + Encode(text) + "</span>");
        }

        /// <summary>Avatar with initials + name, with an optional second line.</summary>
        public static IHtmlContent Person(string? name, string? note = null)
        {
            var html = "<span class=\"who\"><span class=\"avatar\" aria-hidden=\"true\">" + Encode(Initials(name)) + "</span><span><b>" + Encode(name) + "</b>";
            if (!string.IsNullOrEmpty(note)) { html += "<small>" + Encode(note) + "</small>"; }
            return new HtmlString(html + "</span></span>");
        }

        /// <summary>Icon name (wwwroot/img/icons.svg) for an attachment.</summary>
        public static string FileIcon(string? fileName)
        {
            switch (Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant())
            {
                case ".jpg":
                case ".jpeg":
                case ".png":
                    return "file-image";
                case ".xls":
                case ".xlsx":
                case ".csv":
                    return "file-spreadsheet";
                case ".pdf":
                case ".doc":
                case ".docx":
                case ".txt":
                    return "file-text";
                default:
                    return "file";
            }
        }

        /// <summary>Column heading that sorts the list; a second click reverses the order.</summary>
        public static IHtmlContent SortLink(IUrlHelper url, string action, string label, string field,
            string? currentField, bool ascending, int pageSize, string? search)
        {
            var on = string.Equals(field, currentField, StringComparison.Ordinal);
            var href = url.Action(action, new { rowperpage = pageSize, searchString = search, sortField = field, sortAscending = on ? !ascending : true });
            var iconName = on ? (ascending ? "chevron-up" : "chevron-down") : "chevrons-up-down";
            var hint = on ? (ascending ? ", sorted ascending" : ", sorted descending") : string.Empty;

            return new HtmlString(
                "<a class=\"th-sort" + (on ? " on" : string.Empty) + "\" data-list-link href=\"" + Encode(href) + "\" title=\"Sort by " + Encode(label.ToLowerInvariant()) + "\">"
                + Encode(label) + "<span class=\"sr-only\">" + hint + "</span>" + Icon(url, iconName) + "</a>");
        }

        /// <summary>Same markup as the &lt;icon&gt; tag helper, for html built in code.</summary>
        public static string Icon(IUrlHelper url, string name)
        {
            return "<svg class=\"icon\" aria-hidden=\"true\"><use href=\"" + Encode(url.Content("~/img/icons.svg")) + "?v=" + IconTagHelper.SpriteVersion + "#" + Encode(name) + "\"></use></svg>";
        }

        private static string Encode(string? text)
        {
            return WebUtility.HtmlEncode(text ?? string.Empty);
        }
    }
}
