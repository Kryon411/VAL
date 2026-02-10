using System;
using System.Collections.Generic;

namespace VAL.Host.Commands
{
    /// <summary>
    /// Central list of known WebView -> Host commands.
    ///
    /// "Schema-lite" philosophy:
    /// - We only define required fields when it protects correctness.
    /// - Handlers still do their own defensive checks.
    /// - Unknown commands are ignored to keep the host resilient.
    /// </summary>
    public sealed class CommandRegistry
    {
        private readonly Dictionary<string, CommandSpec> _specs =
            new(StringComparer.OrdinalIgnoreCase);

        public void Register(CommandSpec spec)
        {
            if (spec == null || string.IsNullOrWhiteSpace(spec.Type))
                return;

            _specs[spec.Type.Trim()] = spec;
        }

        public bool TryDispatch(HostCommand cmd, out Exception? exception)
        {
            exception = null;

            if (string.IsNullOrWhiteSpace(cmd.Type))
                return false;

            // 1) Exact match.
            if (_specs.TryGetValue(cmd.Type, out var spec))
            {
                if (!ValidateRequiredFields(cmd, spec.RequiredFields))
                    return false;

                try
                {
                    spec.Handler(cmd);
                    return true;
                }
                catch (Exception ex)
                {
                    exception = ex;
                    return false;
                }
            }

            return false;
        }

        private static bool ValidateRequiredFields(HostCommand cmd, string[] required)
        {
            if (required == null || required.Length == 0)
                return true;

            foreach (var field in required)
            {
                if (string.IsNullOrWhiteSpace(field)) continue;

                // If it's required, it must exist (any value kind). Use TryGetString/bool only when needed.
                try
                {
                    // Prefer root-level fields...
                    if (cmd.Root.TryGetProperty(field, out _))
                        continue;

                    // ...but allow required fields under a "payload" object too.
                    if (cmd.Root.TryGetProperty("payload", out var payload) &&
                        payload.ValueKind == System.Text.Json.JsonValueKind.Object &&
                        payload.TryGetProperty(field, out _))
                        continue;

                    return false;
                }
                catch
                {
                    return false;
                }
            }

            return true;
        }
    }
}
