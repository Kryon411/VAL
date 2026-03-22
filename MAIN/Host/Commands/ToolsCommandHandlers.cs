using System;
using System.Threading;
using VAL.Contracts;
using VAL.Host;
using VAL.Host.Logging;
using VAL.Host.Services;

namespace VAL.Host.Commands
{
    internal sealed class ToolsCommandHandlers
    {
        private readonly IUiThread _uiThread;
        private readonly ITruthHealthWindowService _truthHealthWindowService;
        private readonly IDiagnosticsWindowService _diagnosticsWindowService;
        private readonly RateLimiter _rateLimiter = new();
        private static readonly TimeSpan LogInterval = TimeSpan.FromSeconds(10);
        private static int _diagnosticsFailureToastShown;

        public ToolsCommandHandlers(
            IUiThread uiThread,
            ITruthHealthWindowService truthHealthWindowService,
            IDiagnosticsWindowService diagnosticsWindowService)
        {
            _uiThread = uiThread ?? throw new ArgumentNullException(nameof(uiThread));
            _truthHealthWindowService = truthHealthWindowService ?? throw new ArgumentNullException(nameof(truthHealthWindowService));
            _diagnosticsWindowService = diagnosticsWindowService ?? throw new ArgumentNullException(nameof(diagnosticsWindowService));
        }

        public void HandleOpenTruthHealth(HostCommand cmd)
        {
            try
            {
                _uiThread.Invoke(_truthHealthWindowService.ShowTruthHealth);
            }
            catch (Exception ex)
            {
                LogCommandFailure("open_truth_health", cmd, ex);
            }
        }

        public void HandleOpenDiagnostics(HostCommand cmd)
        {
            try
            {
                ValLog.Info(nameof(ToolsCommandHandlers), "Tools: Diagnostics requested");
                _uiThread.Invoke(_diagnosticsWindowService.ShowDiagnostics);
            }
            catch (Exception ex)
            {
                ReportDiagnosticsFailure(cmd, ex, "exception");
            }
        }

        private void LogCommandFailure(string action, HostCommand cmd, Exception ex)
        {
            var key = $"cmd.fail.tools.{action}";
            if (!_rateLimiter.Allow(key, LogInterval))
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
