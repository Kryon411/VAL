using System;
using System.Threading;
using VAL.Contracts;
using VAL.Host.Logging;
using VAL.Host.Services;

namespace VAL.Host.Commands
{
    internal sealed class CommandDiagnosticsReporter : ICommandDiagnosticsReporter
    {
        private int _diagnosticsFailureToastShown;
        private readonly ILog _log;
        private readonly IToastService _toastService;

        public CommandDiagnosticsReporter(IToastService toastService, ILog log)
        {
            _toastService = toastService ?? throw new ArgumentNullException(nameof(toastService));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public void ReportDiagnosticsFailure(HostCommand? cmd, Exception? exception, string reason)
        {
            var sourceHost = cmd?.SourceUri?.Host ?? "unknown";
            var cmdType = cmd?.Type ?? WebCommandNames.ToolsOpenDiagnostics;
            var detail = exception != null
                ? LogSanitizer.Sanitize(exception.ToString())
                : "No exception details.";
            _log.Warn(nameof(CommandDiagnosticsReporter),
                $"Diagnostics command failed ({reason}) for {cmdType} (source: {sourceHost}). {detail}");
            ShowDiagnosticsFailureToast();
        }

        private void ShowDiagnosticsFailureToast()
        {
            if (Interlocked.Exchange(ref _diagnosticsFailureToastShown, 1) == 1)
                return;

            try
            {
                _toastService.ShowMessage(
                    "Diagnostics failed (see Logs/VAL.log)",
                    null,
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
