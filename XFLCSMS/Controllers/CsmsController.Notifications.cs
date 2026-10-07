using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using XFLCSMS.Infrastructure;
using XFLCSMS.Models.Admin;
using XFLCSMS.Models.Notify;
using XFLCSMS.Services;
using XFLCSMS.Services.Notify;

namespace XFLCSMS.Controllers
{
    // The bell: what the signed-in user was told. Every action works on the notifications of that user only.
    //   NotificationStream   the open page listens here; a new notification shows up as a toast at once
    //   NotificationsPeek    the newest ones, for the panel under the bell
    //   Notifications        the whole list, and what the user wants to get (in the application, e-mail, SMS)
    public abstract partial class CsmsController
    {
        private static readonly JsonSerializerOptions LiveJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        private static readonly TimeSpan StreamLife = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan StreamPing = TimeSpan.FromSeconds(20);

        private NotificationHub Hub => HttpContext.RequestServices.GetRequiredService<NotificationHub>();

        private TimeSpan SessionTimeout => HttpContext.RequestServices.GetRequiredService<IOptions<SessionOptions>>().Value.IdleTimeout;

        /// <summary>
        /// Server-sent events for the open page: "hello" (unread count), "missed" for what arrived while no page was
        /// listening, then one "notice" per new notification.
        /// The browser reconnects by itself; the stream ends after five minutes, when the user has not clicked
        /// anything for as long as the session lasts, and when the page goes away.
        /// </summary>
        /// <param name="after">The newest notification the browser has already shown.</param>
        [HttpGet]
        public async Task NotificationStream(long after = 0)
        {
            var me = CurrentUser!;
            // ends when the page goes away, and when the application stops (an open stream must not hold up a restart)
            using var ending = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted,
                HttpContext.RequestServices.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping);
            var aborted = ending.Token;
            if (!NotificationEvents.ChannelOn(Settings, NotificationEvents.InAppChannel))
            {
                Response.StatusCode = StatusCodes.Status204NoContent; // tells the browser not to come back
                return;
            }

            // a reconnect says what it saw last
            if (long.TryParse(Request.Headers["Last-Event-ID"].FirstOrDefault(), out var seen) && seen > after)
            {
                after = seen;
            }

            var unread = await Db.Notifications.CountAsync(n => n.UserId == me.Id && n.ReadAt == null, aborted);
            var last = await Db.Notifications.Where(n => n.UserId == me.Id).MaxAsync(n => (long?)n.Id, aborted) ?? 0;
            var missed = after > 0 && after < last
                ? await Db.Notifications.Where(n => n.UserId == me.Id && n.Id > after && n.ReadAt == null).OrderByDescending(n => n.Id).Take(3).ToListAsync(aborted)
                : new List<Notification>();

            var hub = Hub;
            var sessionId = HttpContext.Session.Id;
            var timeout = SessionTimeout;
            var subscriber = hub.Subscribe(me.Id);
            if (subscriber == null)
            {
                // this account already listens on as many pages as it may: this page goes without live messages
                Response.StatusCode = StatusCodes.Status204NoContent;
                return;
            }

            Response.ContentType = "text/event-stream";
            Response.Headers["X-Accel-Buffering"] = "no";
            HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
            try
            {
                await Response.WriteAsync("retry: 4000\nevent: hello\ndata: " + JsonSerializer.Serialize(new { unread, last }, LiveJson) + "\n\n", aborted);
                foreach (var notice in missed.OrderBy(n => n.Id))
                {
                    await WriteNoticeAsync(NotificationService.ToLive(notice), aborted, "missed");
                }

                await Response.Body.FlushAsync(aborted);

                var started = DateTime.UtcNow;
                while (!aborted.IsCancellationRequested && DateTime.UtcNow - started < StreamLife)
                {
                    using var wait = CancellationTokenSource.CreateLinkedTokenSource(aborted);
                    wait.CancelAfter(StreamPing);
                    try
                    {
                        var notice = await subscriber.Queue.Reader.ReadAsync(wait.Token);
                        await WriteNoticeAsync(notice, aborted);
                    }
                    catch (OperationCanceledException) when (!aborted.IsCancellationRequested)
                    {
                        if (hub.IsIdle(sessionId, timeout))
                        {
                            // nobody has used this session for as long as it lasts: the next click, on any page, signs out
                            await Response.WriteAsync("event: bye\ndata: idle\n\n", aborted);
                            await Response.Body.FlushAsync(aborted);
                            return;
                        }

                        await Response.WriteAsync(": ping\n\n", aborted);
                    }
                    catch (ChannelClosedException)
                    {
                        return; // replaced by a newer stream of the same user
                    }

                    await Response.Body.FlushAsync(aborted);
                }
            }
            catch (OperationCanceledException)
            {
                // the page was closed
            }
            catch (IOException)
            {
                // the connection broke
            }
            finally
            {
                hub.Unsubscribe(subscriber);
            }
        }

        /// <param name="kind">"notice": it happened just now. "missed": it happened while no page was listening.</param>
        private Task WriteNoticeAsync(LiveNotice notice, CancellationToken cancel, string kind = "notice")
        {
            return Response.WriteAsync("id: " + notice.Id + "\nevent: " + kind + "\ndata: " + JsonSerializer.Serialize(notice, LiveJson) + "\n\n", cancel);
        }

        /// <summary>The newest notifications as a piece of page, for the panel under the bell.</summary>
        [HttpGet]
        public async Task<IActionResult> NotificationsPeek()
        {
            var me = CurrentUser!;
            var items = await Db.Notifications.Where(n => n.UserId == me.Id).OrderByDescending(n => n.Id).Take(8).ToListAsync();
            Response.Headers["X-Unread"] = (await Db.Notifications.CountAsync(n => n.UserId == me.Id && n.ReadAt == null)).ToString();
            return PartialView("_NotificationItems", items);
        }

        [HttpGet]
        public async Task<IActionResult> Notifications(int page = 1, bool unread = false)
        {
            try
            {
                const int pageSize = 20;
                var me = CurrentUser!;
                var settings = Settings;
                var mine = Db.Notifications.Where(n => n.UserId == me.Id);
                var shown = unread ? mine.Where(n => n.ReadAt == null) : mine;
                var total = await shown.CountAsync();
                var pages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
                page = Math.Min(Math.Max(page, 1), pages);
                var wish = await Db.NotificationPreferences.FirstOrDefaultAsync(item => item.UserId == me.Id);
                var account = await Db.Users.Where(u => u.Id == me.Id).Select(u => new { u.Email, u.PhonNumber }).FirstAsync();

                return View(new NotificationsView
                {
                    Items = await shown.OrderByDescending(n => n.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(),
                    Unread = await mine.CountAsync(n => n.ReadAt == null),
                    Total = total,
                    Page = page,
                    Pages = pages,
                    OnlyUnread = unread,
                    WantInApp = wish?.InApp ?? true,
                    WantEmail = wish?.Email ?? true,
                    WantSms = wish?.Sms ?? true,
                    InAppOn = NotificationEvents.ChannelOn(settings, NotificationEvents.InAppChannel),
                    EmailOn = NotificationEvents.ChannelOn(settings, NotificationEvents.EmailChannel),
                    SmsOn = NotificationEvents.ChannelOn(settings, NotificationEvents.SmsChannel),
                    MyEmail = account.Email,
                    MyPhone = account.PhonNumber
                });
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        /// <summary>Opens the page a notification points to and marks it read.</summary>
        [HttpGet]
        public async Task<IActionResult> OpenNotification(long id)
        {
            try
            {
                var me = CurrentUser!;
                var notice = await Db.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.UserId == me.Id);
                if (notice == null)
                {
                    return RedirectToAction("Notifications");
                }

                if (notice.ReadAt == null)
                {
                    notice.ReadAt = DateTime.Now;
                    await Db.SaveChangesAsync();
                }

                // only pages a notification is ever written for; anything else stays on the list
                var pages = new[] { "TicketView", "EditUser", "UserView", "UserList", "NotificationSettings" };
                var action = pages.FirstOrDefault(name => name == notice.LinkAction);
                if (action == null)
                {
                    return RedirectToAction("Notifications");
                }

                // the ticket may be gone since (deleted, or no longer visible for this role): say so instead of "page not found"
                if (action == "TicketView")
                {
                    var ticket = await Db.Issues.FirstOrDefaultAsync(i => i.IssueId == notice.LinkId);
                    if (ticket == null || !CanAccessIssue(ticket))
                    {
                        TempData["ErrorMessage"] = "The ticket of that notification no longer exists, or you can no longer see it.";
                        return RedirectToAction("Notifications");
                    }
                }

                return notice.LinkId == null ? RedirectToAction(action) : RedirectToAction(action, new { id = notice.LinkId });
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        /// <summary>Marks every notification of the signed-in user as read.</summary>
        [HttpPost]
        public async Task<IActionResult> ReadNotifications()
        {
            try
            {
                var me = CurrentUser!;
                var now = DateTime.Now;
                var open = await Db.Notifications.Where(n => n.UserId == me.Id && n.ReadAt == null).ToListAsync();
                foreach (var notice in open) { notice.ReadAt = now; }
                await Db.SaveChangesAsync();

                if (SessionAuthorizeAttribute.IsAjax(Request))
                {
                    return Ok();
                }

                return RedirectToAction("Notifications");
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        /// <summary>What the signed-in user wants to get. A box that is not ticked is not posted, so missing means off.</summary>
        [HttpPost]
        public async Task<IActionResult> SaveNotificationPreferences(bool inApp = false, bool email = false, bool sms = false)
        {
            try
            {
                var me = CurrentUser!;
                var wish = await Db.NotificationPreferences.FirstOrDefaultAsync(item => item.UserId == me.Id);
                if (wish == null)
                {
                    wish = new NotificationPreference { UserId = me.Id };
                    Db.NotificationPreferences.Add(wish);
                }

                wish.InApp = inApp;
                wish.Email = email;
                wish.Sms = sms;
                await Db.SaveChangesAsync();
                TempData["SuccessMessage"] = "Your notification preferences are saved.";
                return RedirectToAction("Notifications");
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }
    }
}
