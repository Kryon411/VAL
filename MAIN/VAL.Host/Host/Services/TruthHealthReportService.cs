using System;
using System.IO;
using VAL.Continuum.Pipeline.Truth;
using VAL.Host;
using VAL.Host.Logging;
using VAL.Host.Security;

namespace VAL.Host.Services
{
    internal sealed class TruthHealthReportService : ITruthHealthReportService
    {
        internal const int WarnMb = 50;
        private static readonly TimeSpan LogInterval = TimeSpan.FromSeconds(20);
        private static readonly RateLimiter RateLimiter = new();

        private readonly string? _productRootOverride;

        public TruthHealthReportService()
        {
        }

        internal TruthHealthReportService(string? productRootOverride)
        {
            _productRootOverride = productRootOverride;
        }

        TruthHealthSnapshotResult ITruthHealthReportService.GetCurrentSnapshot()
        {
            return GetCurrentSnapshot();
        }

        internal TruthHealthSnapshotResult GetCurrentSnapshot()
        {
            var chatId = TruthSession.CurrentChatId;
            var hasChatId = !string.IsNullOrWhiteSpace(chatId) && Guid.TryParse(chatId, out _);
            var productRoot = ResolveProductRoot(chatId);
            var reports = BuildReports(productRoot);

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

                var report = TruthHealth.Build(chatId, truthPath, repairLogPath, repairTailFirst: false);
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

        private static System.Collections.Generic.List<TruthHealthSnapshot> BuildReports(string? productRoot)
        {
            var reports = new System.Collections.Generic.List<TruthHealthSnapshot>();
            if (string.IsNullOrWhiteSpace(productRoot))
                return reports;

            try
            {
                var chatsRoot = Path.Combine(productRoot, "Memory", "Chats");
                if (!Directory.Exists(chatsRoot))
                    return reports;

                var chatDirs = Directory.GetDirectories(chatsRoot);
                Array.Sort(chatDirs, StringComparer.OrdinalIgnoreCase);

                foreach (var chatDir in chatDirs)
                {
                    var chatId = Path.GetFileName(chatDir);
                    if (string.IsNullOrWhiteSpace(chatId))
                        continue;

                    var truthPath = Path.Combine(chatDir, TruthStorage.TruthFileName);
                    var repairLogPath = Path.Combine(chatDir, "Truth.repair.log");
                    var report = TruthHealth.Build(chatId, truthPath, repairLogPath, repairTailFirst: false);
                    var relativePath = Path.Combine("Memory", "Chats", chatId, TruthStorage.TruthFileName);
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

        private string? ResolveProductRoot(string? chatId)
        {
            if (!string.IsNullOrWhiteSpace(_productRootOverride))
                return _productRootOverride;

            if (string.IsNullOrWhiteSpace(chatId))
                return null;

            try
            {
                var chatDir = TruthStorage.GetChatDir(chatId);
                var chatsRoot = Directory.GetParent(chatDir);
                var memoryRoot = chatsRoot?.Parent;
                var productRoot = memoryRoot?.Parent;
                return productRoot?.FullName;
            }
            catch
            {
                return null;
            }
        }

        private bool TryResolvePaths(string chatId, out string truthPath, out string repairLogPath, out string relativePath)
        {
            truthPath = string.Empty;
            repairLogPath = string.Empty;
            relativePath = Path.Combine("Memory", "Chats", chatId, TruthStorage.TruthFileName);

            if (!string.IsNullOrWhiteSpace(_productRootOverride))
            {
                if (!SafePathResolver.TryResolveChatTruthPath(_productRootOverride, chatId, out truthPath, out var chatDir))
                    return false;

                repairLogPath = Path.Combine(chatDir, "Truth.repair.log");
                return true;
            }

            truthPath = TruthStorage.GetTruthPath(chatId);
            var dir = Path.GetDirectoryName(truthPath) ?? string.Empty;
            repairLogPath = Path.Combine(dir, "Truth.repair.log");
            return true;
        }

        private static void LogFailure()
        {
            if (!RateLimiter.Allow("truth.health.report", LogInterval))
                return;

            ValLog.Warn(nameof(TruthHealthReportService), "Truth health report generation failed.");
        }
    }
}
