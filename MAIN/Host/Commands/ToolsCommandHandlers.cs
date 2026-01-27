using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using VAL.Host.Logging;
using VAL.Host.Services;

namespace VAL.Host.Commands
{
    internal static class ToolsCommandHandlers
    {
        private static readonly RateLimiter RateLimiter = new();
        private static readonly TimeSpan LogInterval = TimeSpan.FromSeconds(10);

        public static void HandleOpenTruthHealth(HostCommand cmd)
        {
            try
            {
                var services = GetServices();
                if (services == null)
                    return;

                var uiThread = services.GetRequiredService<IUiThread>();
                var truthHealth = services.GetRequiredService<ITruthHealthWindowService>();
                uiThread.Invoke(truthHealth.ShowTruthHealth);
            }
            catch (Exception ex)
            {
                LogCommandFailure("open_truth_health", cmd, ex);
            }
        }

        public static void HandleOpenDiagnostics(HostCommand cmd)
        {
            try
            {
                var services = GetServices();
                if (services == null)
                    return;

                var uiThread = services.GetRequiredService<IUiThread>();
                var diagnostics = services.GetRequiredService<IDiagnosticsWindowService>();
                uiThread.Invoke(diagnostics.ShowDiagnostics);
            }
            catch (Exception ex)
            {
                LogCommandFailure("open_diagnostics", cmd, ex);
            }
        }

        private static IServiceProvider? GetServices()
        {
            return (Application.Current as App)?.Services;
        }

        private static void LogCommandFailure(string action, HostCommand cmd, Exception ex)
        {
            var key = $"cmd.fail.tools.{action}";
            if (!RateLimiter.Allow(key, LogInterval))
                return;

            var sourceHost = cmd.SourceUri?.Host ?? "unknown";
            ValLog.Warn(nameof(ToolsCommandHandlers),
                $"Tools command failed ({action}) for {cmd.Type} (source: {sourceHost}). {ex.GetType().Name}: {LogSanitizer.Sanitize(ex.Message)}");
        }
    }
}
