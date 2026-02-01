using System;
using System.Threading;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using VAL.Contracts;
using VAL.Host;
using VAL.Host.Logging;
using VAL.Host.Services;

namespace VAL.Host.Commands
{
    internal static class ToolsCommandHandlers
    {
        private static readonly RateLimiter RateLimiter = new();
        private static readonly TimeSpan LogInterval = TimeSpan.FromSeconds(10);
        private static int _diagnosticsFailureToastShown;

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
                ValLog.Info(nameof(ToolsCommandHandlers), "Tools: Diagnostics requested");
                var services = GetServices();
                if (services == null)
                {
                    ReportDiagnosticsFailure(cmd, null, "services_unavailable");
                    return;
                }

                var uiThread = services.GetRequiredService<IUiThread>();
                var diagnostics = services.GetRequiredService<IDiagnosticsWindowService>();
                uiThread.Invoke(diagnostics.ShowDiagnostics);
            }
            catch (Exception ex)
            {
                ReportDiagnosticsFailure(cmd, ex, "exception");
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
                $"Tools command failed ({action}) for {cmd.Type} (source: {sourceHost}). {LogSanitizer.Sanitize(ex.ToString())}");
        }

        internal static void ReportDiagnosticsFailure(HostCommand? cmd, Exception? ex, string reason)
        {
            var sourceHost = cmd?.SourceUri?.Host ?? "unknown";
            var cmdType = cmd?.Type ?? WebCommandNames.ToolsOpenDiagnostics;
            var detail = ex != null
                ? LogSanitizer.Sanitize(ex.ToString())
                : "No exception details.";
            ValLog.Warn(nameof(ToolsCommandHandlers),
                $"Diagnostics command failed ({reason}) for {cmdType} (source: {sourceHost}). {detail}");
            ShowDiagnosticsFailureToast();
        }

        internal static void ReportDiagnosticsFailure(string? sourceHost, Exception? ex, string reason)
        {
            var host = string.IsNullOrWhiteSpace(sourceHost) ? "unknown" : sourceHost;
            var detail = ex != null
                ? LogSanitizer.Sanitize(ex.ToString())
                : "No exception details.";
            ValLog.Warn(nameof(ToolsCommandHandlers),
                $"Diagnostics command failed ({reason}) (source: {host}). {detail}");
            ShowDiagnosticsFailureToast();
        }

        private static void ShowDiagnosticsFailureToast()
        {
            if (Interlocked.Exchange(ref _diagnosticsFailureToastShown, 1) == 1)
                return;

            try
            {
                ToastManager.ShowCatalog(
                    "Diagnostics failed (see Logs/VAL.log)",
                    null,
                    ToastManager.ToastDurationBucket.M,
                    groupKey: "tools.diagnostics",
                    replaceGroup: true,
                    bypassBurstDedupe: true);
            }
            catch
            {
                // Toast failures should not crash the app.
            }
        }
    }
}
