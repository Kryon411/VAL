using System;
using VAL.Host.Logging;

namespace VAL.Host
{
    public sealed class ValLog : ILog
    {
        private static readonly ValLogRuntime Runtime = new();

        public static ValLog Instance { get; } = new ValLog();

        private ValLog()
        {
        }

        public static void Configure(string? logPath, bool enableVerboseLogging)
        {
            Runtime.Configure(logPath, enableVerboseLogging);
        }

        // Convenience entry point for callers that don't carry an ILog instance.
        // NOTE: static/instance is NOT part of the C# member signature, so we cannot also expose
        // a public instance method named Warn(category, message) with the same parameters.
        public static void Info(string category, string message)
        {
            Runtime.Info(category, message);
        }

        public static void Warn(string category, string message)
        {
            Runtime.Warn(category, message);
        }

        public static void Error(string category, string message)
        {
            Runtime.Error(category, message);
        }

        public static void Verbose(string category, string message)
        {
            Runtime.Verbose(category, message);
        }

        public static void AddSink(ILogSink sink)
        {
            Runtime.AddSink(sink);
        }

        public static IReadOnlyList<LogEvent> GetRecent(int max = 200)
        {
            return Runtime.GetRecent(max);
        }

        // ILog implementation (explicit) to avoid colliding with the static Warn method above.
        void ILog.Info(string category, string message)
        {
            Runtime.Info(category, message);
        }

        void ILog.Warn(string category, string message)
        {
            Runtime.Warn(category, message);
        }

        void ILog.LogError(string category, string message)
        {
            Runtime.Error(category, message);
        }

        void ILog.Verbose(string category, string message)
        {
            Runtime.Verbose(category, message);
        }
    }
}
