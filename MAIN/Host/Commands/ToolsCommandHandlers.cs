using System;
using VAL.Host.Logging;
using VAL.Host.Services;

namespace VAL.Host.Commands
{
    internal sealed class ToolsCommandHandlers
    {
        private readonly IUiThread _uiThread;
        private readonly ITruthHealthWindowService _truthHealthWindowService;
        private readonly IDiagnosticsWindowService _diagnosticsWindowService;
        private readonly ICommandDiagnosticsReporter _diagnosticsReporter;
        private readonly RateLimiter _rateLimiter = new();
        private static readonly TimeSpan LogInterval = TimeSpan.FromSeconds(10);

        public ToolsCommandHandlers(
            IUiThread uiThread,
            ITruthHealthWindowService truthHealthWindowService,
            IDiagnosticsWindowService diagnosticsWindowService,
            ICommandDiagnosticsReporter diagnosticsReporter)
        {
            _uiThread = uiThread ?? throw new ArgumentNullException(nameof(uiThread));
            _truthHealthWindowService = truthHealthWindowService ?? throw new ArgumentNullException(nameof(truthHealthWindowService));
            _diagnosticsWindowService = diagnosticsWindowService ?? throw new ArgumentNullException(nameof(diagnosticsWindowService));
            _diagnosticsReporter = diagnosticsReporter ?? throw new ArgumentNullException(nameof(diagnosticsReporter));
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
                _diagnosticsReporter.ReportDiagnosticsFailure(cmd, ex, "exception");
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
    }
}
