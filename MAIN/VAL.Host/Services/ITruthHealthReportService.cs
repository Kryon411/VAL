namespace VAL.Host.Services
{
    public sealed record TruthHealthReport(
        string ChatId,
        long Bytes,
        int PhysicalLineCount,
        int ParsedEntryCount,
        int LastParsedPhysicalLineNumber,
        System.DateTime? LastRepairUtc,
        long? LastRepairBytesRemoved);

    public sealed record TruthHealthSnapshot(
        TruthHealthReport Report,
        string RelativeTruthPath,
        bool IsLargeLog);

    public sealed record TruthHealthSnapshotResult(
        bool HasActiveChat,
        string ChatId,
        string StatusMessage,
        TruthHealthSnapshot? Snapshot,
        System.Collections.Generic.IReadOnlyList<TruthHealthSnapshot> Reports);

    public interface ITruthHealthReportService
    {
        TruthHealthSnapshotResult GetCurrentSnapshot();
    }
}
