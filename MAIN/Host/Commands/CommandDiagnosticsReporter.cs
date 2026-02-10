using System;

namespace VAL.Host.Commands
{
    internal sealed class CommandDiagnosticsReporter : ICommandDiagnosticsReporter
    {
        public void ReportDiagnosticsFailure(HostCommand? cmd, Exception? exception, string reason)
        {
            ToolsCommandHandlers.ReportDiagnosticsFailure(cmd, exception, reason);
        }
    }
}
