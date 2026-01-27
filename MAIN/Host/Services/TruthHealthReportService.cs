using System;
using System.IO;
using VAL.Continuum.Pipeline.Truth;
using VAL.Host;
using VAL.Host.Logging;
using VAL.Host.Security;

namespace VAL.Host.Services
{
    public sealed class TruthHealthReportService : ITruthHealthReportService
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

        public TruthHealthSnapshotResult GetCurrentSnapshot()
        {
            var chatId = TruthSession.CurrentChatId;
            if (string.IsNullOrWhiteSpace(chatId) || !Guid.TryParse(chatId, out _))
            {
                return new TruthHealthSnapshotResult(
                    HasActiveChat: false,
                    ChatId: string.Empty,
                    StatusMessage: "No active chat session detected.",
                    Snapshot: null);
            }

            return BuildSnapshot(chatId);
        }

        internal TruthHealthSnapshotResult BuildSnapshot(string chatId)
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
                        Snapshot: null);
                }

                var report = TruthHealth.Build(chatId, truthPath, repairLogPath);
                var isLargeLog = report.Bytes > WarnMb * 1024L * 1024L;
                var snapshot = new TruthHealthSnapshot(report, relativePath, isLargeLog);

                return new TruthHealthSnapshotResult(
                    HasActiveChat: true,
                    ChatId: chatId,
                    StatusMessage: string.Empty,
                    Snapshot: snapshot);
            }
            catch
            {
                LogFailure();
                return new TruthHealthSnapshotResult(
                    HasActiveChat: true,
                    ChatId: chatId,
                    StatusMessage: "Truth health unavailable.",
                    Snapshot: null);
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
