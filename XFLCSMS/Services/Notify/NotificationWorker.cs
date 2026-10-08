using XFLCSMS.Models.Email;
using XFLCSMS.Models.Notify;

namespace XFLCSMS.Services.Notify
{
    /// <summary>
    /// Sends the queued e-mails and SMS (table NotificationDeliveries) in the background. Woken as soon as a
    /// request has queued something, and looks by itself every 30 seconds (retries, rows left from before a
    /// restart). A message that fails is tried again after 1, 5 and 30 minutes; then it stays "failed" and the
    /// settings page shows why.
    /// </summary>
    public class NotificationWorker : BackgroundService
    {
        public const int MaxAttempts = 4;
        private static readonly TimeSpan[] RetryAfter = { TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30) };
        private const int KeepDays = 90;

        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<NotificationWorker> _logger;
        private readonly SemaphoreSlim _signal = new(0, 1);
        private DateTime _lastCleanUp = DateTime.MinValue;

        public NotificationWorker(IServiceScopeFactory scopes, ILogger<NotificationWorker> logger)
        {
            _scopes = scopes;
            _logger = logger;
        }

        /// <summary>When the worker last looked at the queue (for the system health page).</summary>
        public DateTime? LastRun { get; private set; }

        public void Wake()
        {
            try
            {
                if (_signal.CurrentCount == 0) { _signal.Release(); }
            }
            catch (SemaphoreFullException)
            {
                // somebody else woke it at the same moment
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken); // let the application start first
                while (!stoppingToken.IsCancellationRequested)
                {
                    var more = false;
                    try
                    {
                        more = await SendDueAsync(stoppingToken);
                        LastRun = DateTime.Now;
                        if (DateTime.UtcNow - _lastCleanUp > TimeSpan.FromHours(6))
                        {
                            _lastCleanUp = DateTime.UtcNow;
                            await CleanUpAsync(stoppingToken);
                        }
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception exception)
                    {
                        _logger.LogError(exception, "The notification sender could not work through its queue");
                    }

                    if (!more)
                    {
                        await _signal.WaitAsync(TimeSpan.FromSeconds(30), stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // the application is stopping
            }
        }

        /// <summary>Sends up to 20 messages that are due. True when there may be more.</summary>
        public async Task<bool> SendDueAsync(CancellationToken cancel)
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DataContext>();
            var settings = scope.ServiceProvider.GetRequiredService<SettingsStore>();
            var now = DateTime.Now;

            var due = await db.NotificationDeliveries
                .Where(item => item.Status == NotificationDelivery.Queued && (item.NextTryAt == null || item.NextTryAt <= now))
                .OrderBy(item => item.Id)
                .Take(20)
                .ToListAsync(cancel);

            // a channel whose server does not answer is left alone for the rest of this round: with a dead mail
            // server every further mail would cost its full time-out, and the SMS behind it would wait
            var down = new HashSet<string>();
            foreach (var item in due)
            {
                if (cancel.IsCancellationRequested)
                {
                    break; // what was sent so far is recorded; the rest stays queued
                }

                string? error;
                if (!NotificationEvents.ChannelOn(settings, item.Channel == NotificationDelivery.Sms ? NotificationEvents.SmsChannel : NotificationEvents.EmailChannel))
                {
                    Finish(item, NotificationDelivery.Skipped, "The channel was switched off before the message went out.");
                }
                else if (down.Contains(item.Channel))
                {
                    item.NextTryAt = DateTime.Now.AddMinutes(1);
                }
                else if (IsDemoAddress(item.Recipient, item.Channel))
                {
                    Finish(item, NotificationDelivery.Skipped, "Demo account: nothing is sent to this address.");
                }
                else
                {
                    try
                    {
                        if (item.Channel == NotificationDelivery.Sms)
                        {
                            error = await scope.ServiceProvider.GetRequiredService<SmsSender>().SendAsync(item.Recipient, item.Body, cancel);
                        }
                        else
                        {
                            scope.ServiceProvider.GetRequiredService<IEmailServices>()
                                .SendEmail(new EmailDto { To = item.Recipient, Subject = item.Subject ?? "Xpert CSMS", Body = item.Body });
                            error = null;
                        }
                    }
                    catch (OperationCanceledException) when (cancel.IsCancellationRequested)
                    {
                        break; // stopped in the middle of this one: it was not sent, it stays queued
                    }
                    catch (Exception exception)
                    {
                        error = XFLCSMS.EmailService.EmailService.Describe(exception);
                        // everything except "this one message was refused" means the server itself is the problem
                        if (!(exception is MailKit.Net.Smtp.SmtpCommandException) && !(exception is FormatException) && !(exception is MimeKit.ParseException))
                        {
                            down.Add(item.Channel);
                        }
                    }

                    if (error != null && (error.StartsWith(SmsSender.Unreachable, StringComparison.Ordinal) || error.StartsWith(SmsSender.NoAnswer, StringComparison.Ordinal)))
                    {
                        down.Add(item.Channel);
                    }

                    item.Attempts++;
                    if (error == null)
                    {
                        Finish(item, NotificationDelivery.Sent, null);
                    }
                    else if (item.Attempts >= MaxAttempts)
                    {
                        Finish(item, NotificationDelivery.Failed, error);
                        _logger.LogWarning("Gave up on the {Channel} to {Recipient} after {Attempts} attempts: {Error}", item.Channel, item.Recipient, item.Attempts, error);
                    }
                    else
                    {
                        item.Error = Cut(error);
                        item.NextTryAt = DateTime.Now + RetryAfter[Math.Min(item.Attempts, RetryAfter.Length) - 1];
                    }
                }

                // recorded even when the application is stopping right now: a message that went out must never be
                // sent a second time after the restart
                await db.SaveChangesAsync(CancellationToken.None);
            }

            return due.Count == 20 && !cancel.IsCancellationRequested;
        }

        private static void Finish(NotificationDelivery item, string status, string? note)
        {
            item.Status = status;
            item.Error = Cut(note);
            item.DoneAt = DateTime.Now;
            item.NextTryAt = null;
        }

        /// <summary>Addresses of the demo accounts (and other reserved names) never get mail or SMS.</summary>
        public static bool IsDemoAddress(string recipient, string channel)
        {
            var text = recipient.Trim().ToLowerInvariant();
            if (channel == NotificationDelivery.Sms)
            {
                return text.StartsWith("+000", StringComparison.Ordinal) || text.StartsWith("000", StringComparison.Ordinal);
            }

            return text.EndsWith(".invalid", StringComparison.Ordinal) || text.EndsWith("@example.com", StringComparison.Ordinal) || text.EndsWith(".example", StringComparison.Ordinal);
        }

        /// <summary>Notifications and sent messages older than 90 days go.</summary>
        private async Task CleanUpAsync(CancellationToken cancel)
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DataContext>();
            var before = DateTime.Now.AddDays(-KeepDays);

            var notices = await db.Notifications.Where(item => item.At < before).OrderBy(item => item.Id).Take(500).ToListAsync(cancel);
            var messages = await db.NotificationDeliveries.Where(item => item.CreatedAt < before && item.Status != NotificationDelivery.Queued).OrderBy(item => item.Id).Take(500).ToListAsync(cancel);
            if (notices.Count + messages.Count > 0)
            {
                db.Notifications.RemoveRange(notices);
                db.NotificationDeliveries.RemoveRange(messages);
                await db.SaveChangesAsync(cancel);
            }
        }

        private static string? Cut(string? text)
        {
            return string.IsNullOrEmpty(text) || text.Length <= 500 ? text : text.Substring(0, 499) + "…";
        }
    }
}
