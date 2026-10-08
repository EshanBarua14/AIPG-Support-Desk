using System.Collections.Concurrent;

namespace XFLCSMS.Services
{
    /// <summary>
    /// Counts failed attempts per key over a sliding window, in memory. Used for what cannot be counted on an
    /// account: tries from one network address, and tries with names that have no account. (Wrong passwords for an
    /// existing account are counted on the account itself, so they survive a restart and an administrator sees them.)
    /// One instance per application; a restart forgets the counts, which only ever helps the person locked out.
    /// </summary>
    public sealed class AttemptGuard
    {
        private readonly ConcurrentDictionary<string, Queue<DateTime>> _failures = new(StringComparer.OrdinalIgnoreCase);
        private DateTime _nextSweep = DateTime.UtcNow.AddMinutes(10);

        /// <summary>How many failures the key has collected inside the window.</summary>
        public int Count(string key, TimeSpan window)
        {
            if (!_failures.TryGetValue(key, out var times))
            {
                return 0;
            }

            lock (times)
            {
                Trim(times, DateTime.UtcNow - window);
                return times.Count;
            }
        }

        /// <summary>True when the key has reached the limit: the caller refuses the attempt without looking at it.</summary>
        public bool IsBlocked(string key, int limit, TimeSpan window)
        {
            return Count(key, window) >= limit;
        }

        /// <summary>One more failure for the key. Returns the number inside the window, this one included.</summary>
        public int Fail(string key, TimeSpan window)
        {
            var now = DateTime.UtcNow;
            var times = _failures.GetOrAdd(key, _ => new Queue<DateTime>());
            int count;
            lock (times)
            {
                Trim(times, now - window);
                times.Enqueue(now);
                // a flood cannot grow the queue without end: beyond a thousand the oldest fall out
                while (times.Count > 1000)
                {
                    times.Dequeue();
                }

                count = times.Count;
            }

            Sweep(now);
            return count;
        }

        /// <summary>The key behaved (signed in, used a right token): forget its failures.</summary>
        public void Clear(string key)
        {
            _failures.TryRemove(key, out _);
        }

        private static void Trim(Queue<DateTime> times, DateTime oldest)
        {
            while (times.Count > 0 && times.Peek() < oldest)
            {
                times.Dequeue();
            }
        }

        /// <summary>Now and then: drop the keys whose failures are all older than a day, so made-up names do not pile up.</summary>
        private void Sweep(DateTime now)
        {
            if (now < _nextSweep)
            {
                return;
            }

            _nextSweep = now.AddMinutes(10);
            var oldest = now.AddDays(-1);
            foreach (var pair in _failures)
            {
                bool empty;
                lock (pair.Value)
                {
                    Trim(pair.Value, oldest);
                    empty = pair.Value.Count == 0;
                }

                if (empty)
                {
                    _failures.TryRemove(pair.Key, out _);
                }
            }
        }
    }

    /// <summary>The limits on guessing, read from the settings ("SignIn" in appsettings.json) with safe bounds.</summary>
    public sealed class SignInLimits
    {
        public SignInLimits(IConfiguration configuration)
        {
            MaxFailuresPerAccount = Math.Clamp(configuration.GetValue("SignIn:MaxFailuresPerAccount", 5), 3, 50);
            LockMinutes = Math.Clamp(configuration.GetValue("SignIn:LockMinutes", 15), 1, 1440);
            MaxFailuresPerAddress = Math.Clamp(configuration.GetValue("SignIn:MaxFailuresPerAddress", 30), 5, 10000);
            AddressWindowMinutes = Math.Clamp(configuration.GetValue("SignIn:AddressWindowMinutes", 10), 1, 1440);
        }

        /// <summary>Wrong passwords or tokens in a row before an account is locked.</summary>
        public int MaxFailuresPerAccount { get; }

        /// <summary>How long a locked account stays locked.</summary>
        public int LockMinutes { get; }

        /// <summary>Failed attempts from one network address inside the window before that address is refused.</summary>
        public int MaxFailuresPerAddress { get; }

        public int AddressWindowMinutes { get; }

        public TimeSpan LockTime => TimeSpan.FromMinutes(LockMinutes);
        public TimeSpan AddressWindow => TimeSpan.FromMinutes(AddressWindowMinutes);
    }
}
