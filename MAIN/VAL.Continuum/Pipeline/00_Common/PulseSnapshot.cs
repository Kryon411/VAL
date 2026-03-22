using System;
using System.Collections.Generic;
using System.Linq;
using VAL.Continuum.Pipeline.Filter1;
using VAL.Continuum.Pipeline.Truth;

namespace VAL.Continuum.Pipeline
{
    internal sealed class PulseSnapshot
    {
        public string ChatId { get; init; } = string.Empty;
        public TruthView TruthView { get; init; } = new();
        public IReadOnlyList<Filter1BuildSeed.SeedExchange> Filter1Exchanges { get; init; } = Array.Empty<Filter1BuildSeed.SeedExchange>();
        public string SeedLogText { get; init; } = string.Empty;
        public int FrozenBoundaryLineIndex { get; init; } = -1;
        public int FrozenMessageCount { get; init; }

        public static PulseSnapshot Freeze(string chatId, TruthView truthView, int frozenBoundaryLineIndex)
        {
            ArgumentNullException.ThrowIfNull(truthView);

            var effectiveChatId = string.IsNullOrWhiteSpace(chatId) ? truthView.ChatId ?? string.Empty : chatId.Trim();
            var effectiveBoundary = ResolveBoundaryLineIndex(truthView.Messages, frozenBoundaryLineIndex);
            var frozenMessages = FilterMessagesForFrozenBoundary(truthView.Messages, effectiveBoundary);

            var frozenTruthView = new TruthView
            {
                ChatId = effectiveChatId,
                RawText = truthView.RawText,
                ViewText = truthView.ViewText,
                Messages = frozenMessages
            };

            var seed = Filter1BuildSeed.BuildSeed(frozenTruthView);

            return new PulseSnapshot
            {
                ChatId = effectiveChatId,
                TruthView = frozenTruthView,
                Filter1Exchanges = seed.Exchanges,
                SeedLogText = seed.SeedLogText,
                FrozenBoundaryLineIndex = effectiveBoundary,
                FrozenMessageCount = frozenMessages.Count
            };
        }

        private static int ResolveBoundaryLineIndex(IReadOnlyList<TruthMessage> messages, int requestedBoundary)
        {
            if (requestedBoundary >= 0)
                return requestedBoundary;

            if (messages == null || messages.Count == 0)
                return -1;

            var max = -1;
            foreach (var message in messages)
            {
                if (message == null)
                    continue;

                if (message.LineIndex > max)
                    max = message.LineIndex;
            }

            return max;
        }

        private static IReadOnlyList<TruthMessage> FilterMessagesForFrozenBoundary(IReadOnlyList<TruthMessage> messages, int frozenBoundaryLineIndex)
        {
            if (messages == null || messages.Count == 0)
                return Array.Empty<TruthMessage>();

            var filtered = new List<TruthMessage>(messages.Count);
            for (int i = 0; i < messages.Count; i++)
            {
                var message = messages[i];
                if (message == null)
                    continue;

                if (!IsWithinFrozenBoundary(message, frozenBoundaryLineIndex))
                    continue;

                if (LooksLikePulseOrchestrationMessage(message))
                    continue;

                filtered.Add(message);
            }

            return filtered;
        }

        private static bool IsWithinFrozenBoundary(TruthMessage message, int frozenBoundaryLineIndex)
        {
            if (message == null)
                return false;

            if (frozenBoundaryLineIndex < 0)
                return true;

            if (message.LineIndex < 0)
                return true;

            return message.LineIndex <= frozenBoundaryLineIndex;
        }

        private static bool LooksLikePulseOrchestrationMessage(TruthMessage message)
        {
            var normalized = Normalize(message?.Text);
            if (string.IsNullOrWhiteSpace(normalized))
                return false;

            if (message?.Role == TruthRole.User)
                return LooksLikeSignalPrompt(normalized);

            if (message?.Role == TruthRole.Assistant)
                return LooksLikeSignalOutput(normalized);

            return LooksLikeSignalPrompt(normalized) || LooksLikeSignalOutput(normalized);
        }

        private static bool LooksLikeSignalPrompt(string normalized)
        {
            if (string.IsNullOrWhiteSpace(normalized))
                return false;

            return normalized.StartsWith("CONTINUUM SIGNAL INPUT (EXCLUDE FROM CONTINUITY)", StringComparison.Ordinal) ||
                   normalized.StartsWith("Prepare a VAL Pulse handoff for a new chat.", StringComparison.Ordinal) ||
                   normalized.StartsWith("Prepare a compact semantic handoff summary from the frozen Continuum snapshot below.", StringComparison.Ordinal) ||
                   (normalized.Contains("Output only these sections.", StringComparison.Ordinal) &&
                    normalized.Contains("PREVIOUS CHAT SUMMARY", StringComparison.Ordinal) &&
                    normalized.Contains("OPEN LOOPS", StringComparison.Ordinal) &&
                    normalized.Contains("CRITICAL CONTEXT", StringComparison.Ordinal));
        }

        private static bool LooksLikeSignalOutput(string normalized)
        {
            if (string.IsNullOrWhiteSpace(normalized))
                return false;

            return normalized.StartsWith("VAL Pulse Handoff", StringComparison.Ordinal) ||
                   (normalized.StartsWith("PREVIOUS CHAT SUMMARY", StringComparison.Ordinal) &&
                    normalized.Contains("\nOPEN LOOPS\n", StringComparison.Ordinal) &&
                    normalized.Contains("\nCRITICAL CONTEXT\n", StringComparison.Ordinal));
        }

        private static string Normalize(string? text)
            => (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Trim();
    }
}
