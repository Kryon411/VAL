using System.Threading;

namespace VAL.Continuum.Pipeline.Truth
{
    public static class TruthStorage
    {
        public const string TruthFileName = "Truth.log";

        // Compatibility facade for older callers and tests. The product runtime should
        // prefer the injected TruthStore singleton so mutable append/rebuild state has an owner.
        private static readonly TruthStore LegacyStore = new(NullTruthTelemetryPublisher.Instance);

        public static bool TryBeginTruthRebuild(string chatId, bool backupExisting, out string backupPath, out string tempTruthPath, CancellationToken token)
            => LegacyStore.TryBeginTruthRebuild(chatId, backupExisting, out backupPath, out tempTruthPath, token);

        public static void AbortTruthRebuild(string chatId)
            => LegacyStore.AbortTruthRebuild(chatId);

        public static void AbortAllTruthRebuilds()
            => LegacyStore.AbortAllTruthRebuilds();

        public static bool TryCommitTruthRebuild(string chatId)
            => LegacyStore.TryCommitTruthRebuild(chatId);

        public static string GetChatDir(string chatId)
            => LegacyStore.GetChatDir(chatId);

        public static string GetTruthPath(string chatId)
            => LegacyStore.GetTruthPath(chatId);

        public static string EnsureChatDir(string chatId)
            => LegacyStore.EnsureChatDir(chatId);

        public static bool TryResetTruthLog(string chatId, bool backupExisting, out string backupPath)
            => LegacyStore.TryResetTruthLog(chatId, backupExisting, out backupPath);

        public static bool AppendTruthLine(string chatId, char role, string text)
            => LegacyStore.AppendTruthLine(chatId, role, text);

        public static string ReadTruthText(string chatId)
            => LegacyStore.ReadTruthText(chatId);

        public static string[] ReadTruthLines(string chatId)
            => LegacyStore.ReadTruthLines(chatId);
    }
}
