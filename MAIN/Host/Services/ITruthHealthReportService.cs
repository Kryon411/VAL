using VAL.Continuum.Pipeline.Truth;

namespace VAL.Host.Services
{
    internal sealed record TruthHealthSnapshot(
        TruthHealthReport Report,
        string RelativeTruthPath,
        bool IsLargeLog);

    internal sealed record TruthHealthSnapshotResult(
        bool HasActiveChat,
        string ChatId,
        string StatusMessage,
        TruthHealthSnapshot? Snapshot);

    internal interface ITruthHealthReportService
    {
        TruthHealthSnapshotResult GetCurrentSnapshot();
    }
}
