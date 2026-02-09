using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VAL.Host.Logging;

namespace VAL.Host
{
    public sealed class ValLog : ILog
    {
        private static readonly object _sync = new();
        private static readonly List<ILogSink> _sinks = new();
        private static readonly LogEvent[] _recent = new LogEvent[RecentCapacity];
        private static string _logPath = ResolveLogPath();
        private static bool _verboseEnabled;
        private static RollingFileLogSink? _primarySink;
        private static int _recentIndex;
        private static int _recentCount;
        private const int RecentCapacity = 200;

        public static ValLog Instance { get; } = new ValLog();

        static ValLog()
        {
            EnsurePrimarySink();
        }

        private ValLog()
        {
        }

        public static void Configure(string? logPath, bool enableVerboseLogging)
        {
            if (!string.IsNullOrWhiteSpace(logPath))
                _logPath = logPath;

            _verboseEnabled = enableVerboseLogging;
            EnsurePrimarySink();
        }

        // Convenience entry point for callers that don't carry an ILog instance.
        // NOTE: static/instance is NOT part of the C# member signature, so we cannot also expose
        // a public instance method named Warn(category, message) with the same parameters.
        public static void Info(string category, string message)
        {
            WriteCore(LogLevel.Info, category, message);
        }

        public static void Warn(string category, string message)
        {
            WriteCore(LogLevel.Warn, category, message);
        }

        public static void Error(string category, string message)
        {
            WriteCore(LogLevel.Error, category, message);
        }

        public static void Verbose(string category, string message)
        {
            WriteCore(LogLevel.Verbose, category, message);
        }

        internal static void AddSink(ILogSink sink)
        {
            if (sink == null)
                return;

            lock (_sync)
            {
                if (_sinks.Contains(sink))
                    return;

                if (sink is RollingFileLogSink rolling &&
                    _sinks.OfType<RollingFileLogSink>()
                        .Any(existing => string.Equals(existing.FilePath, rolling.FilePath, StringComparison.OrdinalIgnoreCase)))
                    return;

                _sinks.Add(sink);
            }
        }

        internal static IReadOnlyList<LogEvent> GetRecent(int max = RecentCapacity)
        {
            if (max <= 0)
                return Array.Empty<LogEvent>();

            lock (_sync)
            {
                var take = Math.Min(_recentCount, Math.Min(max, RecentCapacity));
                var items = new List<LogEvent>(take);
                if (take == 0)
                    return items;

                var start = (_recentIndex - _recentCount + RecentCapacity) % RecentCapacity;
                for (var i = 0; i < take; i++)
                {
                    var idx = (start + i) % RecentCapacity;
                    var entry = _recent[idx];
                    if (entry != null)
                        items.Add(entry);
                }

                return items;
            }
        }

        // ILog implementation (explicit) to avoid colliding with the static Warn method above.
        void ILog.Info(string category, string message)
        {
            WriteCore(LogLevel.Info, category, message);
        }

        void ILog.Warn(string category, string message)
        {
            WriteCore(LogLevel.Warn, category, message);
        }

        void ILog.LogError(string category, string message)
        {
            WriteCore(LogLevel.Error, category, message);
        }

        void ILog.Verbose(string category, string message)
        {
            WriteCore(LogLevel.Verbose, category, message);
        }

        private static void WriteCore(LogLevel level, string category, string message)
        {
            if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(message))
                return;

            if (level == LogLevel.Verbose && !_verboseEnabled)
                return;

            try
            {
                var timestamp = DateTimeOffset.Now;
                var line = $"{timestamp:yyyy-MM-dd HH:mm:ss.fff} [{level.ToString().ToUpperInvariant()}] [{category}] {message}{Environment.NewLine}";
                var logEvent = new LogEvent(timestamp, level, category, message, line);
                ILogSink[] sinks;

                lock (_sync)
                {
                    RecordRecent(logEvent);
                    sinks = _sinks.ToArray();
                }

                foreach (var sink in sinks)
                {
                    try
                    {
                        sink.Write(logEvent);
                    }
                    catch
                    {
                        // Logging must never throw.
                    }
                }
            }
            catch
            {
                // Logging must never throw.
            }
        }

        private static string ResolveLogPath()
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(root, "VAL", "Logs", "VAL.log");
        }

        private static void EnsurePrimarySink()
        {
            lock (_sync)
            {
                if (_primarySink != null && string.Equals(_primarySink.FilePath, _logPath, StringComparison.OrdinalIgnoreCase))
                    return;

                if (_primarySink != null)
                    _sinks.Remove(_primarySink);

                _primarySink = new RollingFileLogSink(_logPath);
                _sinks.Add(_primarySink);
            }
        }

        private static void RecordRecent(LogEvent logEvent)
        {
            _recent[_recentIndex] = logEvent;
            _recentIndex = (_recentIndex + 1) % RecentCapacity;
            if (_recentCount < RecentCapacity)
                _recentCount++;
        }
    }
}
