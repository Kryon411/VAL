using System;
using System.IO;
using System.Text;

namespace VAL.Host
{
    public sealed class ValLog : ILog
    {
        private static readonly object _sync = new();
        private static string _logPath = ResolveLogPath();
        private static bool _verboseEnabled;

        public static ValLog Instance { get; } = new ValLog();

        private ValLog()
        {
        }

        public static void Configure(string? logPath, bool enableVerboseLogging)
        {
            if (!string.IsNullOrWhiteSpace(logPath))
                _logPath = logPath;

            _verboseEnabled = enableVerboseLogging;
        }

        // Convenience entry point for callers that don't carry an ILog instance.
        // NOTE: static/instance is NOT part of the C# member signature, so we cannot also expose
        // a public instance method named Warn(category, message) with the same parameters.
        public static void Info(string category, string message)
        {
            Instance.WriteCore(ValLogLevel.Info, category, message);
        }

        public static void Warn(string category, string message)
        {
            Instance.WriteCore(ValLogLevel.Warn, category, message);
        }

        public static void Error(string category, string message)
        {
            Instance.WriteCore(ValLogLevel.Error, category, message);
        }

        public static void Verbose(string category, string message)
        {
            Instance.WriteCore(ValLogLevel.Verbose, category, message);
        }

        // ILog implementation (explicit) to avoid colliding with the static Warn method above.
        void ILog.Info(string category, string message)
        {
            WriteCore(ValLogLevel.Info, category, message);
        }

        void ILog.Warn(string category, string message)
        {
            WriteCore(ValLogLevel.Warn, category, message);
        }

        void ILog.Error(string category, string message)
        {
            WriteCore(ValLogLevel.Error, category, message);
        }

        void ILog.Verbose(string category, string message)
        {
            WriteCore(ValLogLevel.Verbose, category, message);
        }

        private void WriteCore(ValLogLevel level, string category, string message)
        {
            if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(message))
                return;

            if (level == ValLogLevel.Verbose && !_verboseEnabled)
                return;

            try
            {
                var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [{level.ToString().ToUpperInvariant()}] [{category}] {message}{Environment.NewLine}";
                lock (_sync)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_logPath) ?? ".");
                    File.AppendAllText(_logPath, line, Encoding.UTF8);
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

        private enum ValLogLevel
        {
            Verbose,
            Info,
            Warn,
            Error
        }
    }
}
