using System.Collections.Concurrent;

namespace XFLCSMS.Infrastructure
{
    /// <summary>
    /// Keeps the last warnings and errors the application has logged since it started, in memory, so the
    /// system health page can show them without access to the server's console or log files.
    /// </summary>
    public sealed class RecentLog : ILoggerProvider
    {
        public sealed record Entry(DateTime At, LogLevel Level, string Category, string Message);

        public const int Capacity = 200;

        public static readonly RecentLog Instance = new();

        private readonly ConcurrentQueue<Entry> _entries = new();

        private RecentLog()
        {
        }

        /// <summary>Newest first.</summary>
        public IReadOnlyList<Entry> Entries => _entries.Reverse().ToList();

        public DateTime StartedAt { get; } = DateTime.Now;

        private void Add(Entry entry)
        {
            _entries.Enqueue(entry);
            while (_entries.Count > Capacity && _entries.TryDequeue(out _))
            {
            }
        }

        public ILogger CreateLogger(string categoryName) => new Sink(this, categoryName);

        public void Dispose()
        {
        }

        private sealed class Sink : ILogger
        {
            private readonly RecentLog _owner;
            private readonly string _category;

            public Sink(RecentLog owner, string category)
            {
                _owner = owner;
                _category = category;
            }

            // Implemented explicitly, so the constraint on TState is the one of the framework in use:
            // .NET 6 has none, later versions have "notnull". (Written out here it gives a warning on one or the other.)
            IDisposable ILogger.BeginScope<TState>(TState state) => Nothing.Instance;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning && logLevel != LogLevel.None;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel))
                {
                    return;
                }

                var message = formatter(state, exception);
                if (exception != null)
                {
                    message += " | " + exception.GetType().Name + ": " + exception.Message;
                }

                if (message.Length > 600)
                {
                    message = message.Substring(0, 600) + "…";
                }

                var dot = _category.LastIndexOf('.');
                _owner.Add(new Entry(DateTime.Now, logLevel, dot >= 0 ? _category.Substring(dot + 1) : _category, message));
            }
        }

        private sealed class Nothing : IDisposable
        {
            public static readonly Nothing Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
