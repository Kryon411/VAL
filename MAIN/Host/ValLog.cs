using System;
using System.IO;
using System.Text;

namespace VAL.Host
{
    public sealed class ValLog : ILog
    {
        private static readonly object _sync = new();
        private static readonly string _logPath = ResolveLogPath();

        public static ValLog Instance { get; } = new ValLog();

        private ValLog()
        {
        }

        public static void Warn(string category, string message)
        {
            Instance.Warn(category, message);
        }

        public void Warn(string category, string message)
        {
            if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(message))
                return;

            try
            {
                var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [{category}] {message}{Environment.NewLine}";
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
    }
}
