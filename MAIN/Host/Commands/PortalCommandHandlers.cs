using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using VAL.Host.Logging;
using VAL.Host.Services;

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
                {
                    var runtime = GetRuntime() ?? new PortalRuntimeStateManager();
                    runtime.SetEnabled(en);
                }
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
                var runtime = GetRuntime() ?? new PortalRuntimeStateManager();
                runtime.OpenSnipOverlay();
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
                var runtime = GetRuntime() ?? new PortalRuntimeStateManager();
                runtime.SendStaged(max);
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

        private static IPortalRuntimeStateManager? GetRuntime()
        {
            return (Application.Current as App)?.Services.GetService<IPortalRuntimeStateManager>();
        }
    }
}
