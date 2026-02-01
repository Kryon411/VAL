using System;
using System.Text.Json;
using VAL.Contracts;
using VAL.Host.Commands;
using VAL.Host.Logging;

namespace VAL.Host.Portal
{
    internal static class PortalHostRouter
    {
        private static readonly RateLimiter RateLimiter = new();
        private static readonly TimeSpan LogInterval = TimeSpan.FromSeconds(10);

        public static bool Handle(HostCommand cmd)
        {
            var t = cmd.Type ?? string.Empty;

            if (t.Equals(WebCommandNames.PortalCommandSetEnabled, StringComparison.OrdinalIgnoreCase))
            {
                bool enabled = false;
                try
                {
                    if (cmd.Root.TryGetProperty("enabled", out var e) &&
                        (e.ValueKind == JsonValueKind.True || e.ValueKind == JsonValueKind.False))
                    {
                        enabled = e.GetBoolean();
                    }
                }
                catch (Exception ex)
                {
                    LogCommandFailure("parse_enabled", cmd, ex);
                }

                try
                {
                    PortalRuntime.SetEnabled(enabled);
                }
                catch (Exception ex)
                {
                    LogCommandFailure("set_enabled", cmd, ex);
                }
                return true;
            }

            if (t.Equals(WebCommandNames.PortalCommandOpenSnip, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    PortalRuntime.OpenSnipOverlay();
                }
                catch (Exception ex)
                {
                    LogCommandFailure("open_snip", cmd, ex);
                }
                return true;
            }

            if (t.Equals(WebCommandNames.PortalCommandSendStaged, StringComparison.OrdinalIgnoreCase))
            {
                int max = 10;
                try
                {
                    if (cmd.Root.TryGetProperty("max", out var m) && m.ValueKind == JsonValueKind.Number)
                        max = Math.Clamp(m.GetInt32(), 1, 10);
                }
                catch (Exception ex)
                {
                    max = 10;
                    LogCommandFailure("parse_max", cmd, ex);
                }

                try
                {
                    PortalRuntime.SendStaged(max);
                }
                catch (Exception ex)
                {
                    LogCommandFailure("send_staged", cmd, ex);
                }
                return true;
            }

            return true; // ignore unknown portal.* commands safely
        }

        private static void LogCommandFailure(string action, HostCommand cmd, Exception ex)
        {
            var key = $"cmd.fail.portal_router.{action}";
            if (!RateLimiter.Allow(key, LogInterval))
                return;

            var sourceHost = cmd.SourceUri?.Host ?? "unknown";
            ValLog.Warn(nameof(PortalHostRouter),
                $"Portal router error ({action}) for {cmd.Type} (source: {sourceHost}). {ex.GetType().Name}: {LogSanitizer.Sanitize(ex.Message)}");
        }
    }
}
