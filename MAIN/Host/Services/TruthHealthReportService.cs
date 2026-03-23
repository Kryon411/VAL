using System;
using System.IO;
using VAL.Continuum.Pipeline.Truth;
using VAL.Host.Logging;
using VAL.Host.Security;
using ContinuumTruthHealthReport = VAL.Continuum.Pipeline.Truth.TruthHealthReport;

namespace VAL.Host.Services
{
    internal sealed class TruthHealthReportService : ITruthHealthReportService
    {
        internal const int WarnMb = 50;
        private static readonly TimeSpan LogInterval = TimeSpan.FromSeconds(20);

        private readonly IAppPaths _appPaths;
        private readonly ISessionContext _sessionContext;
        private readonly ITruthStore _truthStore;
        private readonly string? _productRootOverride;
        private readonly RateLimiter _rateLimiter = new();

        public TruthHealthReportService(IAppPaths appPaths, ISessionContext sessionContext, ITruthStore truthStore)
        {
            _appPaths = appPaths ?? throw new ArgumentNullException(nameof(appPaths));
            _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
            _truthStore = truthStore ?? throw new ArgumentNullException(nameof(truthStore));
        }

        internal TruthHealthReportService(IAppPaths appPaths, ISessionContext sessionContext, ITruthStore truthStore, string? productRootOverride)
        {
            _appPaths = appPaths ?? throw new ArgumentNullException(nameof(appPaths));
            _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
            _truthStore = truthStore ?? throw new ArgumentNullException(nameof(truthStore));
            _productRootOverride = productRootOverride;
        }

        TruthHealthSnapshotResult ITruthHealthReportService.GetCurrentSnapshot()
        {
            return GetCurrentSnapshot();
        }

        internal TruthHealthSnapshotResult GetCurrentSnapshot()
        {
            var chatId = _sessionContext.ActiveChatId;
            var hasChatId = !string.IsNullOrWhiteSpace(chatId) && Guid.TryParse(chatId, out _);
            var memoryChatsRoot = ResolveMemoryChatsRoot();
            var reports = BuildReports(memoryChatsRoot);

            if (!hasChatId)
            {
                return new TruthHealthSnapshotResult(
                    HasActiveChat: false,
                    ChatId: string.Empty,
                    StatusMessage: "No active chat session detected.",
                    Snapshot: null,
                    Reports: reports);
            }

            var snapshotResult = BuildSnapshot(chatId, reports);
            if (snapshotResult.Snapshot != null)
                return snapshotResult;

            return new TruthHealthSnapshotResult(
                HasActiveChat: true,
                ChatId: chatId,
                StatusMessage: snapshotResult.StatusMessage,
                Snapshot: null,
                Reports: reports);
        }

        internal TruthHealthSnapshotResult BuildSnapshot(string chatId, System.Collections.Generic.IReadOnlyList<TruthHealthSnapshot> reports)
        {
            try
            {
                if (!TryResolvePaths(chatId, out var truthPath, out var repairLogPath, out var relativePath))
                {
                    LogFailure();
                    return new TruthHealthSnapshotResult(
                        HasActiveChat: true,
                        ChatId: chatId,
                        StatusMessage: "Truth health unavailable.",
                        Snapshot: null,
                        Reports: reports);
                }

                var report = ToSnapshotReport(TruthHealth.Build(chatId, truthPath, repairLogPath, repairTailFirst: false));
                var isLargeLog = report.Bytes > WarnMb * 1024L * 1024L;
                var snapshot = new TruthHealthSnapshot(report, relativePath, isLargeLog);

                return new TruthHealthSnapshotResult(
                    HasActiveChat: true,
                    ChatId: chatId,
                    StatusMessage: string.Empty,
                    Snapshot: snapshot,
                    Reports: reports);
            }
            catch
            {
                LogFailure();
                return new TruthHealthSnapshotResult(
                    HasActiveChat: true,
                    ChatId: chatId,
                    StatusMessage: "Truth health unavailable.",
                    Snapshot: null,
                    Reports: reports);
            }
        }

        private System.Collections.Generic.List<TruthHealthSnapshot> BuildReports(string? memoryChatsRoot)
        {
            var reports = new System.Collections.Generic.List<TruthHealthSnapshot>();
            if (string.IsNullOrWhiteSpace(memoryChatsRoot))
                return reports;

            try
            {
                if (!Directory.Exists(memoryChatsRoot))
                    return reports;

                var chatDirs = Directory.GetDirectories(memoryChatsRoot);
                Array.Sort(chatDirs, StringComparer.OrdinalIgnoreCase);

                foreach (var chatDir in chatDirs)
                {
                    var chatId = Path.GetFileName(chatDir);
                    if (string.IsNullOrWhiteSpace(chatId))
                        continue;

                    var truthPath = Path.Combine(chatDir, _truthStore.TruthFileName);
                    var repairLogPath = Path.Combine(chatDir, "Truth.repair.log");
                    var report = ToSnapshotReport(TruthHealth.Build(chatId, truthPath, repairLogPath, repairTailFirst: false));
                    var relativePath = Path.Combine("Memory", "Chats", chatId, _truthStore.TruthFileName);
                    var isLargeLog = report.Bytes > WarnMb * 1024L * 1024L;
                    reports.Add(new TruthHealthSnapshot(report, relativePath, isLargeLog));
                }
            }
            catch
            {
                LogFailure();
            }

            return reports;
        }

        private string? ResolveMemoryChatsRoot()
        {
            if (!string.IsNullOrWhiteSpace(_productRootOverride))
                return AppPathLayout.ResolveMemoryChatsRoot(_productRootOverride);

            return _appPaths.MemoryChatsRoot;
        }

        private bool TryResolvePaths(string chatId, out string truthPath, out string repairLogPath, out string relativePath)
        {
            truthPath = string.Empty;
            repairLogPath = string.Empty;
            relativePath = Path.Combine("Memory", "Chats", chatId, _truthStore.TruthFileName);

            if (!string.IsNullOrWhiteSpace(_productRootOverride))
            {
                if (!SafePathResolver.TryResolveChatTruthPath(_productRootOverride, chatId, out truthPath, out var chatDir))
                    return false;

                repairLogPath = Path.Combine(chatDir, "Truth.repair.log");
                return true;
            }

            truthPath = _truthStore.GetTruthPath(chatId);
            var dir = Path.GetDirectoryName(truthPath) ?? string.Empty;
            repairLogPath = Path.Combine(dir, "Truth.repair.log");
            return true;
        }

        private void LogFailure()
        {
            if (!_rateLimiter.Allow("truth.health.report", LogInterval))
                return;

            ValLog.Warn(nameof(TruthHealthReportService), "Truth health report generation failed.");
        }

        private static TruthHealthReport ToSnapshotReport(ContinuumTruthHealthReport report)
        {
            return new TruthHealthReport(
                report.ChatId,
                report.Bytes,
                report.PhysicalLineCount,
                report.ParsedEntryCount,
                report.LastParsedPhysicalLineNumber,
                report.LastRepairUtc,
                report.LastRepairBytesRemoved);
        }
    }
}
