using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace VAL.Host.Logging
{
    internal sealed class ValLogRuntime
    {
        private const int RecentCapacity = 200;

        private readonly object _sync = new();
        private readonly List<ILogSink> _sinks = new();
        private readonly LogEvent[] _recent = new LogEvent[RecentCapacity];
        private string _logPath;
        private bool _verboseEnabled;
        private RollingFileLogSink? _primarySink;
        private int _recentIndex;
        private int _recentCount;

        public ValLogRuntime(string? logPath = null, bool enableVerboseLogging = false)
        {
            _logPath = string.IsNullOrWhiteSpace(logPath) ? ResolveLogPath() : logPath;
            _verboseEnabled = enableVerboseLogging;
            EnsurePrimarySink();
        }

        public void Configure(string? logPath, bool enableVerboseLogging)
        {
            if (!string.IsNullOrWhiteSpace(logPath))
                _logPath = logPath;

            _verboseEnabled = enableVerboseLogging;
            EnsurePrimarySink();
        }

        public void Info(string category, string message)
        {
            WriteCore(LogLevel.Info, category, message);
        }

        public void Warn(string category, string message)
        {
            WriteCore(LogLevel.Warn, category, message);
        }

        public void Error(string category, string message)
        {
            WriteCore(LogLevel.Error, category, message);
        }

        public void Verbose(string category, string message)
        {
            WriteCore(LogLevel.Verbose, category, message);
        }

        public void AddSink(ILogSink sink)
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

        public IReadOnlyList<LogEvent> GetRecent(int max = RecentCapacity)
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

        private void WriteCore(LogLevel level, string category, string message)
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

        private void EnsurePrimarySink()
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

        private void RecordRecent(LogEvent logEvent)
        {
            _recent[_recentIndex] = logEvent;
            _recentIndex = (_recentIndex + 1) % RecentCapacity;
            if (_recentCount < RecentCapacity)
                _recentCount++;
        }
    }
}
