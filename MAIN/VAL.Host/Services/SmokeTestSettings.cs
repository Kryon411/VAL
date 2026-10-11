using System;

namespace VAL.Host.Services
{
    public sealed class SmokeTestSettings
    {
        public const int DefaultTimeoutMs = 15000;

        public bool Enabled { get; }
        public TimeSpan Timeout { get; }
        public string? ReportPath { get; }
        public string? DataRoot { get; }

        public SmokeTestSettings(bool enabled, TimeSpan timeout, string? reportPath, string? dataRoot = null)
        {
            Enabled = enabled;
            Timeout = timeout;
            ReportPath = reportPath;
            DataRoot = dataRoot;
        }

        public static SmokeTestSettings FromArgs(string[]? args)
        {
            args ??= Array.Empty<string>();

            var enabled = false;
            var timeoutMs = DefaultTimeoutMs;
            string? reportPath = null;
            string? dataRoot = null;

            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i] ?? string.Empty;

                if (string.Equals(arg, "--smoke", StringComparison.OrdinalIgnoreCase))
                {
                    enabled = true;
                    continue;
                }

                if (TryParseArgValue(arg, "--smoke-timeout-ms", args, ref i, out var timeoutValue))
                {
                    if (int.TryParse(timeoutValue, out var parsed) && parsed > 0)
                    {
                        timeoutMs = parsed;
                    }
                    continue;
                }

                if (TryParseArgValue(arg, "--smoke-report-path", args, ref i, out var reportValue))
                {
                    if (!string.IsNullOrWhiteSpace(reportValue))
                    {
                        reportPath = reportValue.Trim();
                    }
                }
                if (TryParseArgValue(arg, "--smoke-data-root", args, ref i, out var rootValue))
                    dataRoot = rootValue;
            }

            if (!enabled && dataRoot != null)
                throw new ArgumentException("A smoke data root requires --smoke.");
            return new SmokeTestSettings(enabled, TimeSpan.FromMilliseconds(timeoutMs), reportPath, dataRoot);
        }

        private static bool TryParseArgValue(string arg, string key, string[] args, ref int index, out string? value)
        {
            value = null;

            if (!arg.StartsWith(key, StringComparison.OrdinalIgnoreCase))
                return false;

            if (arg.Length == key.Length)
            {
                if (index + 1 >= args.Length)
                    return true;

                value = args[++index];
                return true;
            }

            if (arg.Length > key.Length && arg[key.Length] == '=')
            {
                value = arg[(key.Length + 1)..];
                return true;
            }

            return false;
        }
    }
}
