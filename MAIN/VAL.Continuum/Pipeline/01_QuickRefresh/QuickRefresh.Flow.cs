using System;
using System.IO;
using System.Threading;
using VAL.Continuum.Pipeline;
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

            var injectSeed = BuildLegacyPulseSeed(chatId, token);
            token.ThrowIfCancellationRequested();
            EssenceInjectInbox.Enqueue(injectSeed);
        }

        public static EssenceInjectController.InjectSeed BuildLegacyPulseSeed(string chatId, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(chatId))
                throw new ArgumentNullException(nameof(chatId));

            token.ThrowIfCancellationRequested();

            var snapshot = BuildPulseSnapshot(chatId, token);
            var deterministicSections = BuildDeterministicPulseSections(chatId, snapshot, token);
            var pulsePacket = BuildDeterministicPulsePacket(snapshot, deterministicSections, token);
            return CreatePulseSeed(chatId, pulsePacket, "PulsePacketComposer", token);
        }

        internal static PulseSnapshot BuildPulseSnapshot(string chatId, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(chatId))
                throw new ArgumentNullException(nameof(chatId));

            token.ThrowIfCancellationRequested();

            var truth = TruthNormalize.BuildView(chatId);
            token.ThrowIfCancellationRequested();

            if (truth.Messages == null || truth.Messages.Count == 0)
                throw new InvalidOperationException("No Truth messages available for this chat.");

            var snapshot = PulseSnapshot.Freeze(chatId, truth, frozenBoundaryLineIndex: -1);
            if (snapshot.Filter1Exchanges == null || snapshot.Filter1Exchanges.Count == 0)
                throw new InvalidOperationException("Filter1 produced no pre-Pulse exchanges.");

            TryWriteAuditText(chatId, "Seed.log", snapshot.SeedLogText);
            return snapshot;
        }

        internal static DeterministicPulseSections BuildDeterministicPulseSections(string chatId, PulseSnapshot snapshot, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(chatId))
                throw new ArgumentNullException(nameof(chatId));

            ArgumentNullException.ThrowIfNull(snapshot);
            token.ThrowIfCancellationRequested();

            var sections = Filter2Restructure.BuildSections(snapshot);
            var auditText = Filter2Restructure.RenderDeterministicSections(sections);
            TryWriteAuditText(chatId, "RestructuredSeed.log", auditText);
            return sections;
        }

        internal static string BuildDeterministicPulsePacket(PulseSnapshot snapshot, DeterministicPulseSections deterministicSections, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            ArgumentNullException.ThrowIfNull(deterministicSections);

            token.ThrowIfCancellationRequested();

            var pulsePacket = PulsePacketComposer.Compose(snapshot, deterministicSections, signalSummary: null);
            if (string.IsNullOrWhiteSpace(pulsePacket))
                throw new InvalidOperationException("Pulse packet composer returned no content.");

            return pulsePacket;
        }

        public static EssenceInjectController.InjectSeed CreatePulseSeed(
            string chatId,
            string pulseText,
            string sourceFileName,
            CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(chatId))
                throw new ArgumentNullException(nameof(chatId));

            var normalizedPulseText = NormalizePulseText(pulseText);
            if (string.IsNullOrWhiteSpace(normalizedPulseText))
                throw new InvalidOperationException("Pulse seed text is empty.");

            token.ThrowIfCancellationRequested();

            const string essenceFileName = "Essence-M.Pulse.txt";
            string essencePath = TryWriteEssenceToContinuumStorage(chatId, normalizedPulseText, out var storedName)
                ? storedName
                : essenceFileName;

            token.ThrowIfCancellationRequested();

            return new EssenceInjectController.InjectSeed
            {
                ChatId = chatId,
                Mode = "Pulse",
                EssenceText = normalizedPulseText,
                OpenNewChat = true,
                SourceFileName = sourceFileName ?? string.Empty,
                EssenceFileName = essencePath
            };
        }

        private static string NormalizePulseText(string pulseText)
        {
            return (pulseText ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Trim();
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
