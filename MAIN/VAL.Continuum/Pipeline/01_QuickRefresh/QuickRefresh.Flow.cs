using System;
using System.IO;
using System.Threading;
using VAL.Continuum.Pipeline;
using VAL.Continuum.Pipeline.Essence;
using VAL.Continuum.Pipeline.Filter1;
using VAL.Continuum.Pipeline.Filter2;
using VAL.Continuum.Pipeline.Truth;
using VAL.Continuum.Pipeline.Inject;

namespace VAL.Continuum.Pipeline.QuickRefresh
{
    public static class QuickRefreshFlow
    {
        public static void Run(string chatId)
        {
            Run(chatId, CancellationToken.None);
        }

        public static void Run(string chatId, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(chatId))
                throw new ArgumentNullException(nameof(chatId));

            token.ThrowIfCancellationRequested();

            // 1) Lossless Truth view (structural only)
            var truth = TruthNormalize.BuildView(chatId);

            token.ThrowIfCancellationRequested();

            if (truth.Messages == null || truth.Messages.Count == 0)
                throw new InvalidOperationException("No Truth messages available for this chat.");

            // 2) Filter 1: Truth -> Seed.log (filtered + sliced)
            var seed = Filter1BuildSeed.BuildSeed(truth);
            if (string.IsNullOrWhiteSpace(seed.SeedLogText))
                throw new InvalidOperationException("Filter1 produced an empty Seed.log.");

            TryWriteAuditText(chatId, "Seed.log", seed.SeedLogText);

            token.ThrowIfCancellationRequested();

            // 3) Filter 2: Seed.log -> RestructuredSeed (reverse-order pack, budgeted)
            var restructured = Filter2Restructure.BuildRestructuredSeed(seed.Exchanges);
            if (string.IsNullOrWhiteSpace(restructured))
                throw new InvalidOperationException("Filter2 produced an empty RestructuredSeed.");

            TryWriteAuditText(chatId, "RestructuredSeed.log", restructured);

            token.ThrowIfCancellationRequested();

            // 4) Essence-M (Pulse) - keep existing mechanical cleaning + layout
            var essenceText = EssenceBuild.BuildEssenceM(chatId, restructured);
            if (string.IsNullOrWhiteSpace(essenceText))
                throw new InvalidOperationException("Essence-M builder returned no content.");

            token.ThrowIfCancellationRequested();

            // 3b) Load Context.txt preamble and inject it into the Essence text for the final seed payload
            var injectText = ContinuumPreamble.InjectIntoEssence(essenceText, chatId);

            token.ThrowIfCancellationRequested();

            // 4) Persist Essence-M (audit / reuse)
            //    Keep path stable with existing Continuum storage conventions.
            //    This uses existing storage if present; otherwise falls back to writing beside Truth.log.
            string essenceFileName = "Essence-M.Pulse.txt";
            string essencePath = TryWriteEssenceToContinuumStorage(chatId, injectText, out var storedName)
                ? storedName
                : essenceFileName;

            token.ThrowIfCancellationRequested();

            // 5) Queue seed for sealed injector (MainWindow timer posts into WebView)
            var injectSeed = new EssenceInjectController.InjectSeed
            {
                ChatId = chatId,
                Mode = "Pulse",
                EssenceText = injectText,
                OpenNewChat = true,
                SourceFileName = "RestructuredSeed",
                EssenceFileName = essencePath
            };

            token.ThrowIfCancellationRequested();
            EssenceInjectInbox.Enqueue(injectSeed);
        }

        /// <summary>
        /// Best-effort audit writer.
        /// Writes small pipeline artifacts (Seed.log, RestructuredSeed.log, etc.) beside Truth.log.
        /// Must never throw.
        /// </summary>
        private static void TryWriteAuditText(string chatId, string fileName, string text)
        {
            if (string.IsNullOrWhiteSpace(chatId)) return;
            if (string.IsNullOrWhiteSpace(fileName)) return;

            try
            {
                var truthPath = TruthStorage.GetTruthPath(chatId);
                var dir = Path.GetDirectoryName(truthPath) ?? AppContext.BaseDirectory;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                var path = Path.Combine(dir, fileName);
                AtomicFile.WriteAllTextAtomic(path, text ?? string.Empty);
            }
            catch
            {
                // audit must never throw
            }
        }

        private static bool TryWriteEssenceToContinuumStorage(string chatId, string essenceText, out string storedFileName)
        {
            // vNext build: keep deterministic and decoupled from legacy storage types.
            // Always write beside Truth.log for audit/reuse.
            storedFileName = "Essence-M.Pulse.txt";

            try
            {
                var truthPath = TruthStorage.GetTruthPath(chatId);
                var dir = Path.GetDirectoryName(truthPath) ?? AppContext.BaseDirectory;
                var path = Path.Combine(dir, storedFileName);
                AtomicFile.WriteAllTextAtomic(path, essenceText);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
