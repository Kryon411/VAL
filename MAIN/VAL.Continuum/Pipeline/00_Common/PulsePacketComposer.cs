using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VAL.Continuum.Pipeline.Filter1;
using VAL.Continuum.Pipeline.Signal;

namespace VAL.Continuum.Pipeline
{
    internal static class PulsePacketComposer
    {
        private static readonly string[] NoneBullets = { "None." };
        internal const string ThreadStateSummaryHeading = "THREAD STATE SUMMARY";
        internal const string WhereWeLeftOffHeading = "WHERE WE LEFT OFF";
        internal const string OpenLoopsHeading = "OPEN LOOPS";
        internal const string CriticalContextHeading = "CRITICAL CONTEXT";
        internal const string TruthWalkbackHighlightsHeading = "TRUTH WALKBACK HIGHLIGHTS";

        internal static string Compose(PulseSnapshot snapshot, DeterministicPulseSections deterministicSections, SignalSummary? signalSummary)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            ArgumentNullException.ThrowIfNull(deterministicSections);

            var sb = new StringBuilder();
            AppendBlock(sb, ContinuumPreamble.BuildPulseContinuityPreamble());
            AppendBulletSection(sb, ThreadStateSummaryHeading, ComposeThreadStateSummary(snapshot, deterministicSections, signalSummary));
            AppendExchangeSection(sb, WhereWeLeftOffHeading, deterministicSections.WhereWeLeftOff);
            AppendBulletSection(sb, OpenLoopsHeading, ComposeOpenLoops(deterministicSections));
            AppendBulletSection(sb, CriticalContextHeading, ComposeCriticalContext(deterministicSections));
            AppendTruthWalkbackSection(sb, deterministicSections.TruthWalkbackHighlights);
            return sb.ToString().Trim();
        }

        private static IReadOnlyList<string> ComposeThreadStateSummary(PulseSnapshot snapshot, DeterministicPulseSections deterministicSections, SignalSummary? signalSummary)
        {
            if (signalSummary?.PreviousChatSummary?.Count > 0)
                return signalSummary.PreviousChatSummary;

            var bullets = new List<string>();
            var exchanges = snapshot.Filter1Exchanges ?? Array.Empty<Filter1BuildSeed.SeedExchange>();
            int start = Math.Max(0, exchanges.Count - 3);
            for (int i = start; i < exchanges.Count; i++)
            {
                var exchange = exchanges[i];
                if (exchange == null)
                    continue;

                var source = BuildSource(exchange);
                var user = BuildSnippet(SelectExchangeText(exchange.UserTextUncut, exchange.UserText), 180);
                var assistant = BuildSnippet(SelectExchangeText(exchange.AssistantTextUncut, exchange.AssistantText), 180);

                if (!string.IsNullOrWhiteSpace(user) && !string.IsNullOrWhiteSpace(assistant))
                {
                    bullets.Add($"{source}: USER {user} ASSISTANT {assistant}");
                }
                else if (!string.IsNullOrWhiteSpace(user))
                {
                    bullets.Add($"{source}: USER {user}");
                }
                else if (!string.IsNullOrWhiteSpace(assistant))
                {
                    bullets.Add($"{source}: ASSISTANT {assistant}");
                }
            }

            if (bullets.Count == 0 && deterministicSections.WhereWeLeftOff != null)
            {
                var user = BuildSnippet(deterministicSections.WhereWeLeftOff.User, 180);
                var assistant = BuildSnippet(deterministicSections.WhereWeLeftOff.Assistant, 180);
                if (!string.IsNullOrWhiteSpace(user))
                    bullets.Add($"Latest user intent: {user}");
                if (!string.IsNullOrWhiteSpace(assistant) && !assistant.Equals("[ASSISTANT: empty]", StringComparison.Ordinal))
                    bullets.Add($"Latest assistant state: {assistant}");
            }

            return EnsureBullets(bullets);
        }

        private static IReadOnlyList<string> ComposeOpenLoops(DeterministicPulseSections deterministicSections)
        {
            return MergeBullets(deterministicSections.OpenLoopFacts);
        }

        private static IReadOnlyList<string> ComposeCriticalContext(DeterministicPulseSections deterministicSections)
        {
            return MergeBullets(deterministicSections.CriticalFacts, deterministicSections.ArtifactsAndReferences);
        }

        private static IReadOnlyList<string> MergeBullets(params IReadOnlyList<string>?[] sources)
        {
            var merged = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var source in sources)
            {
                if (source == null)
                    continue;

                for (int i = 0; i < source.Count; i++)
                {
                    var item = NormalizeBullet(source[i]);
                    if (string.IsNullOrWhiteSpace(item))
                        continue;

                    var key = NormalizeDedupKey(item);
                    if (!seen.Add(key))
                        continue;

                    merged.Add(item);
                }
            }

            return EnsureBullets(merged);
        }

        private static IReadOnlyList<string> EnsureBullets(IReadOnlyList<string> bullets)
        {
            if (bullets == null || bullets.Count == 0)
                return NoneBullets;

            return bullets;
        }

        private static void AppendTruthWalkbackSection(StringBuilder sb, IReadOnlyList<PulseExchangeBlock> walkbackHighlights)
        {
            if (walkbackHighlights == null || walkbackHighlights.Count == 0)
            {
                AppendBulletSection(sb, TruthWalkbackHighlightsHeading, NoneBullets);
                return;
            }

            AppendHeading(sb, TruthWalkbackHighlightsHeading);
            for (int i = 0; i < walkbackHighlights.Count; i++)
            {
                if (i > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine();
                }

                sb.Append(RenderExchangeBlock(walkbackHighlights[i]));
            }
        }

        private static void AppendExchangeSection(StringBuilder sb, string heading, PulseExchangeBlock exchange)
        {
            AppendHeading(sb, heading);
            sb.Append(RenderExchangeBlock(exchange));
        }

        private static string RenderExchangeBlock(PulseExchangeBlock exchange)
        {
            exchange ??= PulseExchangeBlock.Empty;

            var sb = new StringBuilder();
            sb.Append("Source: ").AppendLine(string.IsNullOrWhiteSpace(exchange.Source) ? "Unknown" : exchange.Source.Trim());
            sb.AppendLine("USER:");
            sb.AppendLine(string.IsNullOrWhiteSpace(exchange.User) ? "[USER: empty]" : NormalizeBody(exchange.User));
            sb.AppendLine("ASSISTANT:");
            sb.Append(NormalizeBody(string.IsNullOrWhiteSpace(exchange.Assistant) ? "[ASSISTANT: empty]" : exchange.Assistant));
            return sb.ToString().TrimEnd();
        }

        private static void AppendBulletSection(StringBuilder sb, string heading, IReadOnlyList<string> bullets)
        {
            AppendHeading(sb, heading);
            var items = EnsureBullets(bullets);
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0)
                    sb.AppendLine();

                sb.Append("- ").Append(NormalizeBullet(items[i]));
            }
        }

        private static void AppendHeading(StringBuilder sb, string heading)
        {
            if (sb.Length > 0)
            {
                sb.AppendLine();
                sb.AppendLine();
            }

            sb.AppendLine(heading);
        }

        private static void AppendBlock(StringBuilder sb, string block)
        {
            var normalized = NormalizeBody(block);
            if (string.IsNullOrWhiteSpace(normalized))
                return;

            if (sb.Length > 0)
            {
                sb.AppendLine();
                sb.AppendLine();
            }

            sb.Append(normalized);
        }

        private static string NormalizeBody(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                lines[i] = lines[i].TrimEnd();
            }

            return string.Join("\n", lines).Trim();
        }

        private static string NormalizeBullet(string bullet)
        {
            var normalized = NormalizeBody(bullet);
            if (normalized.StartsWith("- ", StringComparison.Ordinal))
                normalized = normalized.Substring(2).Trim();

            return normalized;
        }

        private static string NormalizeDedupKey(string bullet)
        {
            var normalized = NormalizeBullet(bullet);
            return normalized.ToUpperInvariant();
        }

        private static string BuildSnippet(string text, int maxChars)
        {
            var normalized = NormalizeBody(text);
            if (string.IsNullOrWhiteSpace(normalized))
                return string.Empty;

            var line = normalized.Split('\n').FirstOrDefault(l => !string.IsNullOrWhiteSpace(l)) ?? normalized;
            if (line.Length <= maxChars)
                return line.Trim();

            return line.Substring(0, Math.Max(0, maxChars - 4)).TrimEnd() + " ...";
        }

        private static string BuildSource(Filter1BuildSeed.SeedExchange exchange)
            => $"Truth {FormatTruthRange(exchange.UserLineIndex, exchange.AssistantLineIndex)}";

        private static string FormatTruthRange(int userLineIndex, int assistantLineIndex)
        {
            int min = int.MaxValue;
            int max = int.MinValue;

            if (userLineIndex >= 0)
            {
                min = Math.Min(min, userLineIndex);
                max = Math.Max(max, userLineIndex);
            }

            if (assistantLineIndex >= 0)
            {
                min = Math.Min(min, assistantLineIndex);
                max = Math.Max(max, assistantLineIndex);
            }

            if (min == int.MaxValue || max == int.MinValue)
                return "?-?";

            return $"{min}-{max}";
        }

        private static string SelectExchangeText(string uncut, string sliced)
        {
            if (!string.IsNullOrWhiteSpace(uncut))
                return uncut;

            return sliced ?? string.Empty;
        }
    }
}
