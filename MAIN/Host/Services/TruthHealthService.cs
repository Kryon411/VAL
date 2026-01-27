using System;
using System.IO;
using VAL.Continuum.Pipeline.Truth;
using VAL.Host.Logging;
using VAL.Host.Truth;

namespace VAL.Host.Services
{
    internal sealed class TruthHealthService : ITruthHealthService
    {
        private static readonly RateLimiter RateLimiter = new();
        private static readonly TimeSpan LogInterval = TimeSpan.FromSeconds(30);

        public TruthHealthSnapshot GetSnapshot()
        {
            if (!TruthSession.TryGetCurrentChatId(out var chatId))
                return new TruthHealthSnapshot(false, string.Empty, string.Empty, null);

            var relativePath = BuildRelativePath(chatId);
            try
            {
                var truthPath = TruthStorage.GetTruthPath(chatId);
                var repairLogPath = Path.Combine(TruthStorage.GetChatDir(chatId), "Truth.repair.log");
                var report = TruthHealth.Build(chatId, truthPath, repairLogPath, repairTailFirst: false);
                return new TruthHealthSnapshot(true, chatId, relativePath, report);
            }
            catch (Exception ex)
            {
                LogFailure(ex);
                return new TruthHealthSnapshot(true, chatId, relativePath, null);
            }
        }

        private static string BuildRelativePath(string chatId)
        {
            return Path.Combine("Memory", "Chats", chatId, TruthStorage.TruthFileName);
        }

        private static void LogFailure(Exception ex)
        {
            if (!RateLimiter.Allow("truth.health.report.fail", LogInterval))
                return;

            ValLog.Warn(nameof(TruthHealthService),
                $"Truth health report failed. {ex.GetType().Name}: {LogSanitizer.Sanitize(ex.Message)}");
        }
    }
}
