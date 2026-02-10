namespace VAL.Host.Commands
{
    public interface ICommandDiagnosticsReporter
    {
        void ReportDiagnosticsFailure(HostCommand? cmd, System.Exception? exception, string reason);
    }
}
