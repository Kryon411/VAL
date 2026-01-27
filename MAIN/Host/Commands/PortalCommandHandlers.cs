using System;
using VAL.Host.Logging;

namespace VAL.Host.Commands
{
    internal static class PortalCommandHandlers
    {
        private static readonly RateLimiter RateLimiter = new();
        private static readonly TimeSpan LogInterval = TimeSpan.FromSeconds(10);

        public static void HandleSetEnabled(HostCommand cmd)
        {
            try
            {
                if (cmd.TryGetBool("enabled", out var en))
                    VAL.Host.Portal.PortalRuntime.SetEnabled(en);
            }
            catch (Exception ex)
            {
                LogCommandFailure("set_enabled", cmd, ex);
            }
        }

        public static void HandleOpenSnip(HostCommand cmd)
        {
            try
            {
                VAL.Host.Portal.PortalRuntime.OpenSnipOverlay();
            }
            catch (Exception ex)
            {
                LogCommandFailure("open_snip", cmd, ex);
            }
        }

        public static void HandleSendStaged(HostCommand cmd)
        {
            try
            {
                int max = 10;
                if (cmd.TryGetInt("max", out var m)) max = m;
                VAL.Host.Portal.PortalRuntime.SendStaged(max);
            }
            catch (Exception ex)
            {
                LogCommandFailure("send_staged", cmd, ex);
            }
        }

        private static void LogCommandFailure(string action, HostCommand cmd, Exception ex)
        {
            var key = $"cmd.fail.portal.{action}";
            if (!RateLimiter.Allow(key, LogInterval))
                return;

            var sourceHost = cmd.SourceUri?.Host ?? "unknown";
            ValLog.Warn(nameof(PortalCommandHandlers),
                $"Portal command failed ({action}) for {cmd.Type} (source: {sourceHost}). {ex.GetType().Name}: {LogSanitizer.Sanitize(ex.Message)}");
        }
    }
}
