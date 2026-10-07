using System.Collections.Concurrent;
using System.Threading.Channels;

namespace XFLCSMS.Services.Notify
{
    /// <summary>What the browser gets for one toast.</summary>
    public class LiveNotice
    {
        public long Id { get; set; }
        public string Kind { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string? Body { get; set; }
        /// <summary>True when the notice opens a page (OpenNotification/{Id}).</summary>
        public bool HasLink { get; set; }
        public string At { get; set; } = string.Empty;
    }

    /// <summary>
    /// The open pages that listen for notifications (one stream per visible browser tab), in memory.
    /// Publishing to a user who has no page open does nothing: the notification is in the database and
    /// is shown with the next page.
    /// </summary>
    public class NotificationHub
    {
        public sealed class Subscriber
        {
            internal Subscriber(int userId)
            {
                UserId = userId;
                // a browser that does not read keeps only the newest few
                Queue = Channel.CreateBounded<LiveNotice>(new BoundedChannelOptions(20) { FullMode = BoundedChannelFullMode.DropOldest });
            }

            public int UserId { get; }
            public Channel<LiveNotice> Queue { get; }
        }

        private readonly object _gate = new();
        private readonly Dictionary<int, List<Subscriber>> _subscribers = new();
        private readonly ConcurrentDictionary<string, DateTime> _lastClick = new();

        /// <summary>Most streams one account can hold at a time (one per visible browser tab).</summary>
        public const int MaxStreams = 6;

        /// <summary>A place for one open page, or null when the account already holds <see cref="MaxStreams"/>.</summary>
        public Subscriber? Subscribe(int userId)
        {
            var subscriber = new Subscriber(userId);
            lock (_gate)
            {
                if (!_subscribers.TryGetValue(userId, out var list))
                {
                    list = new List<Subscriber>();
                    _subscribers[userId] = list;
                }

                // The newcomer is turned away, not the oldest: pushing the oldest out would make it reconnect and
                // push out the next one, for ever (an account shared by a whole room of people).
                if (list.Count >= MaxStreams)
                {
                    return null;
                }

                list.Add(subscriber);
            }

            return subscriber;
        }

        public void Unsubscribe(Subscriber subscriber)
        {
            lock (_gate)
            {
                if (_subscribers.TryGetValue(subscriber.UserId, out var list))
                {
                    list.Remove(subscriber);
                    if (list.Count == 0) { _subscribers.Remove(subscriber.UserId); }
                }
            }

            subscriber.Queue.Writer.TryComplete();
        }

        public void Publish(int userId, LiveNotice notice)
        {
            List<Subscriber>? targets = null;
            lock (_gate)
            {
                if (_subscribers.TryGetValue(userId, out var list)) { targets = list.ToList(); }
            }

            if (targets == null) { return; }
            foreach (var target in targets)
            {
                target.Queue.Writer.TryWrite(notice);
            }
        }

        public int OpenStreams
        {
            get { lock (_gate) { return _subscribers.Values.Sum(list => list.Count); } }
        }

        // ---- idle time -----------------------------------------------------------------------------
        // The stream is a request too, and every request keeps a session alive. Without this, an open tab would
        // keep a user signed in for ever. So the pages record when the user last did something himself, and the
        // stream ends once that is longer ago than the session time-out.

        public void Clicked(string sessionId)
        {
            _lastClick[sessionId] = DateTime.UtcNow;
            if (_lastClick.Count > 5000)
            {
                var old = DateTime.UtcNow.AddHours(-12);
                foreach (var pair in _lastClick.Where(pair => pair.Value < old).ToList()) { _lastClick.TryRemove(pair.Key, out _); }
            }
        }

        public bool IsIdle(string sessionId, TimeSpan timeout)
        {
            return !_lastClick.TryGetValue(sessionId, out var at) || DateTime.UtcNow - at > timeout;
        }

        /// <summary>
        /// True when this session did something once and then nothing for longer than the time-out. (A session the
        /// hub has never seen is not "expired": it is new, or the application was restarted.)
        /// </summary>
        public bool HasExpired(string sessionId, TimeSpan timeout)
        {
            return _lastClick.TryGetValue(sessionId, out var at) && DateTime.UtcNow - at > timeout;
        }

        public void Forget(string sessionId)
        {
            _lastClick.TryRemove(sessionId, out _);
        }
    }
}
