using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using VAL.Continuum.Pipeline.Filter1;

namespace VAL.Continuum.Pipeline.Filter2
{
    /// <summary>
    /// Filter 2: turns the filtered Seed exchanges into deterministic sections that Continuum can
    /// render locally. WWLO stays authoritative and deterministic; semantic sections remain optional.
    /// </summary>
    public static class Filter2Restructure
    {
        private static readonly string[] NoneBullets = { "None." };

        private static readonly Regex FileReferenceRegex =
            new(@"(?:[A-Za-z]:\\[^\r\n`""]+?\.(?:cs|js|txt|md|json|toml|csproj)|MAIN[\\/][^\r\n`""]+?\.(?:cs|js|txt|md|json|toml|csproj))",
                RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex NumberedListRegex =
            new(@"^\d+[\.\)]\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly string[] ConstraintKeywords =
        {
            "preserve",
            "keep",
            "do not",
            "must",
            "fallback",
            "deterministic",
            "plain-text",
            "plain text",
            "stable heading",
            "stable headings",
            "freeze",
            "exclude",
            "do not let",
            "authoritative",
            "runtime",
            "testing plan",
            "acceptance criteria"
        };

        internal static DeterministicPulseSections BuildSections(PulseSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);

            var exchanges = snapshot.Filter1Exchanges ?? Array.Empty<Filter1BuildSeed.SeedExchange>();
            if (exchanges.Count == 0)
                return new DeterministicPulseSections();

            int total = exchanges.Count;
            int pin = Math.Min(Filter2Rules.WhereWeLeftOffCount, total);
            var pinnedTail = exchanges.Skip(total - pin).ToList();
            var lastExchange = pinnedTail.Count > 0 ? pinnedTail[pinnedTail.Count - 1] : null;

            return new DeterministicPulseSections
            {
                WhereWeLeftOff = lastExchange != null ? BuildWhereWeLeftOff(lastExchange) : PulseExchangeBlock.Empty,
                TruthWalkbackHighlights = BuildTruthWalkbackHighlights(exchanges, pinnedTail),
                OpenLoopFacts = BuildOpenLoopFacts(exchanges),
                CriticalFacts = BuildCriticalFacts(exchanges),
                ArtifactsAndReferences = BuildArtifactsAndReferences(exchanges)
            };
        }

        public static string BuildRestructuredSeed(IReadOnlyList<Filter1BuildSeed.SeedExchange> exchanges)
        {
            if (exchanges == null || exchanges.Count == 0)
                return string.Empty;

            var snapshot = new PulseSnapshot
            {
                Filter1Exchanges = exchanges,
                FrozenBoundaryLineIndex = InferFrozenBoundaryLineIndex(exchanges)
            };

            return RenderDeterministicSections(BuildSections(snapshot));
        }

        internal static string RenderDeterministicSections(DeterministicPulseSections sections)
        {
            sections ??= new DeterministicPulseSections();

            var sb = new StringBuilder();
            AppendExchangeSection(sb, "WHERE WE LEFT OFF", sections.WhereWeLeftOff);
            AppendBulletSection(sb, "OPEN LOOP FACTS", sections.OpenLoopFacts);
            AppendBulletSection(sb, "CRITICAL FACTS", sections.CriticalFacts);
            AppendBulletSection(sb, "ARTIFACTS AND REFERENCES", sections.ArtifactsAndReferences);
            AppendWalkbackSection(sb, sections.TruthWalkbackHighlights);
            return sb.ToString().Trim();
        }

        private static PulseExchangeBlock BuildWhereWeLeftOff(Filter1BuildSeed.SeedExchange exchange)
        {
            return new PulseExchangeBlock
            {
                Source = BuildSource(exchange),
                User = NormalizeSpeakerText(SelectWwloText(exchange.UserTextUncut, exchange.UserText), emptyFallback: "[USER: empty]"),
                Assistant = NormalizeSpeakerText(SelectWwloText(exchange.AssistantTextUncut, exchange.AssistantText), emptyFallback: "[ASSISTANT: empty]")
            };
        }

        private static List<PulseExchangeBlock> BuildTruthWalkbackHighlights(
            IReadOnlyList<Filter1BuildSeed.SeedExchange> exchanges,
            IReadOnlyList<Filter1BuildSeed.SeedExchange> pinnedTail)
        {
            var highlights = new List<PulseExchangeBlock>();

            if (pinnedTail != null)
            {
                for (int i = pinnedTail.Count - 2; i >= 0; i--)
                {
                    AddHighlight(highlights, pinnedTail[i]);
                }
            }

            int cutoff = Math.Max(0, exchanges.Count - (pinnedTail?.Count ?? 0));
            for (int i = cutoff - 1; i >= 0 && highlights.Count < Filter2Rules.TruthWalkbackMaxExchanges; i--)
            {
                AddHighlight(highlights, exchanges[i]);
            }

            return highlights;
        }

        private static List<string> BuildOpenLoopFacts(IReadOnlyList<Filter1BuildSeed.SeedExchange> exchanges)
        {
            var facts = new List<string>();

            for (int i = exchanges.Count - 1; i >= 0 && facts.Count < Filter2Rules.DeterministicFactMaxItems; i--)
            {
                var exchange = exchanges[i];
                if (exchange == null)
                    continue;

                AddRange(facts, ExtractOpenLoopCandidates(SelectPreferredText(exchange.UserTextUncut, exchange.UserText)));
            }

            if (facts.Count == 0 && exchanges.Count > 0)
            {
                var latest = exchanges[exchanges.Count - 1];
                var user = BuildSnippet(SelectPreferredText(latest.UserTextUncut, latest.UserText));
                if (!string.IsNullOrWhiteSpace(user))
                    facts.Add($"Latest user request: {user}");
            }

            return DeduplicateFacts(facts);
        }

        private static List<string> BuildCriticalFacts(IReadOnlyList<Filter1BuildSeed.SeedExchange> exchanges)
        {
            var facts = new List<string>();

            for (int i = exchanges.Count - 1; i >= 0 && facts.Count < Filter2Rules.DeterministicFactMaxItems; i--)
            {
                var exchange = exchanges[i];
                if (exchange == null)
                    continue;

                AddRange(facts, ExtractConstraintCandidates(SelectPreferredText(exchange.UserTextUncut, exchange.UserText)));
                AddRange(facts, ExtractConstraintCandidates(SelectPreferredText(exchange.AssistantTextUncut, exchange.AssistantText)));
            }

            if (facts.Count == 0)
                facts.Add("Resume from WHERE WE LEFT OFF before using Truth Walkback Highlights.");

            return DeduplicateFacts(facts);
        }

        private static List<string> BuildArtifactsAndReferences(IReadOnlyList<Filter1BuildSeed.SeedExchange> exchanges)
        {
            var refs = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = exchanges.Count - 1; i >= 0 && refs.Count < Filter2Rules.DeterministicFactMaxItems; i--)
            {
                var exchange = exchanges[i];
                if (exchange == null)
                    continue;

                CaptureFileReferences(seen, refs, exchange.UserTextUncut);
                CaptureFileReferences(seen, refs, exchange.AssistantTextUncut);
                CaptureFileReferences(seen, refs, exchange.UserText);
                CaptureFileReferences(seen, refs, exchange.AssistantText);
            }

            return refs;
        }

        private static void AddHighlight(List<PulseExchangeBlock> highlights, Filter1BuildSeed.SeedExchange exchange)
        {
            if (highlights == null || exchange == null)
                return;

            if (highlights.Count >= Filter2Rules.TruthWalkbackMaxExchanges)
                return;

            highlights.Add(new PulseExchangeBlock
            {
                Source = BuildSource(exchange),
                User = NormalizeSpeakerText(exchange.UserText, "[USER: empty]"),
                Assistant = NormalizeSpeakerText(exchange.AssistantText, "[ASSISTANT: empty]")
            });
        }

        private static List<string> ExtractOpenLoopCandidates(string text)
        {
            var candidates = new List<string>();
            var lines = Normalize(text).Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                var line = NormalizeFactLine(lines[i]);
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                if (LooksLikeHeading(line))
                    continue;

                if (LooksLikeFileReference(line))
                    continue;

                if (TryExtractLabeledValue(line, "Task:", out var task))
                {
                    candidates.Add(task);
                    continue;
                }

                if (line.StartsWith("- ", StringComparison.Ordinal) || NumberedListRegex.IsMatch(line))
                {
                    var bullet = StripBullet(line);
                    if (!string.IsNullOrWhiteSpace(bullet) && !LooksLikeFileReference(bullet))
                        candidates.Add(bullet);
                }
            }

            return candidates;
        }

        private static List<string> ExtractConstraintCandidates(string text)
        {
            var candidates = new List<string>();
            var lines = Normalize(text).Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                var line = NormalizeFactLine(lines[i]);
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                if (LooksLikeHeading(line))
                    continue;

                if (LooksLikeFileReference(line))
                    continue;

                if (ContainsConstraintKeyword(line))
                {
                    candidates.Add(StripBullet(line));
                    continue;
                }

                if (TryExtractLabeledValue(line, "Important context:", out var important))
                {
                    candidates.Add(important);
                }
            }

            return candidates;
        }

        private static void CaptureFileReferences(HashSet<string> seen, List<string> refs, string text)
        {
            if (refs == null || seen == null)
                return;

            var normalized = Normalize(text);
            if (string.IsNullOrWhiteSpace(normalized))
                return;

            foreach (Match match in FileReferenceRegex.Matches(normalized))
            {
                if (!match.Success)
                    continue;

                var path = match.Value.Trim();
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                if (!seen.Add(path))
                    continue;

                refs.Add(path);
                if (refs.Count >= Filter2Rules.DeterministicFactMaxItems)
                    return;
            }
        }

        private static List<string> DeduplicateFacts(IReadOnlyList<string> facts)
        {
            if (facts == null || facts.Count == 0)
                return new List<string>();

            var unique = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < facts.Count; i++)
            {
                var fact = NormalizeFactLine(facts[i]);
                if (string.IsNullOrWhiteSpace(fact))
                    continue;

                if (fact.Length > Filter2Rules.DeterministicFactMaxChars)
                    fact = fact.Substring(0, Filter2Rules.DeterministicFactMaxChars - 4).TrimEnd() + " ...";

                if (!seen.Add(fact))
                    continue;

                unique.Add(fact);
                if (unique.Count >= Filter2Rules.DeterministicFactMaxItems)
                    break;
            }

            return unique;
        }

        private static void AppendWalkbackSection(StringBuilder sb, IReadOnlyList<PulseExchangeBlock> walkbackHighlights)
        {
            if (walkbackHighlights == null || walkbackHighlights.Count == 0)
            {
                AppendBulletSection(sb, "TRUTH WALKBACK HIGHLIGHTS", NoneBullets);
                return;
            }

            AppendHeading(sb, "TRUTH WALKBACK HIGHLIGHTS");
            for (int i = 0; i < walkbackHighlights.Count; i++)
            {
                if (i > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine();
                }

                sb.Append(RenderExchange(walkbackHighlights[i]));
            }
        }

        private static void AppendExchangeSection(StringBuilder sb, string heading, PulseExchangeBlock exchange)
        {
            AppendHeading(sb, heading);
            sb.Append(RenderExchange(exchange));
        }

        private static void AppendBulletSection(StringBuilder sb, string heading, IReadOnlyList<string> bullets)
        {
            AppendHeading(sb, heading);
            if (bullets == null || bullets.Count == 0)
            {
                sb.Append("- None.");
                return;
            }

            for (int i = 0; i < bullets.Count; i++)
            {
                if (i > 0)
                    sb.AppendLine();

                sb.Append("- ").Append(NormalizeFactLine(bullets[i]));
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

        private static string RenderExchange(PulseExchangeBlock exchange)
        {
            exchange ??= PulseExchangeBlock.Empty;

            var sb = new StringBuilder();
            sb.Append("Source: ").AppendLine(string.IsNullOrWhiteSpace(exchange.Source) ? "Unknown" : exchange.Source.Trim());
            sb.AppendLine("USER:");
            sb.AppendLine(string.IsNullOrWhiteSpace(exchange.User) ? "[USER: empty]" : Normalize(exchange.User));
            sb.AppendLine("ASSISTANT:");
            sb.Append(Normalize(string.IsNullOrWhiteSpace(exchange.Assistant) ? "[ASSISTANT: empty]" : exchange.Assistant));
            return sb.ToString().TrimEnd();
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

        private static string SelectWwloText(string uncut, string sliced)
        {
            var candidate = Normalize(uncut);
            if (string.IsNullOrWhiteSpace(candidate))
                return Normalize(sliced);

            return candidate.Length <= Filter2Rules.WhereWeLeftOffMaxTextChars
                ? candidate
                : Normalize(sliced);
        }

        private static string SelectPreferredText(string uncut, string sliced)
        {
            var preferred = Normalize(uncut);
            if (!string.IsNullOrWhiteSpace(preferred))
                return preferred;

            return Normalize(sliced);
        }

        private static string NormalizeSpeakerText(string text, string emptyFallback)
        {
            var normalized = Normalize(text);
            return string.IsNullOrWhiteSpace(normalized) ? emptyFallback : normalized;
        }

        private static string BuildSnippet(string text)
        {
            var normalized = Normalize(text);
            if (string.IsNullOrWhiteSpace(normalized))
                return string.Empty;

            var line = normalized.Split('\n').FirstOrDefault(l => !string.IsNullOrWhiteSpace(l)) ?? normalized;
            if (line.Length <= Filter2Rules.DeterministicFactMaxChars)
                return line;

            return line.Substring(0, Filter2Rules.DeterministicFactMaxChars - 4).TrimEnd() + " ...";
        }

        private static string Normalize(string text)
            => (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Trim();

        private static string NormalizeFactLine(string text)
        {
            var normalized = Normalize(text);
            if (normalized.StartsWith("- ", StringComparison.Ordinal))
                normalized = normalized.Substring(2).Trim();
            else if (NumberedListRegex.IsMatch(normalized))
                normalized = NumberedListRegex.Replace(normalized, string.Empty).Trim();

            return normalized;
        }

        private static bool TryExtractLabeledValue(string line, string label, out string value)
        {
            value = string.Empty;
            if (string.IsNullOrWhiteSpace(line) || string.IsNullOrWhiteSpace(label))
                return false;

            if (!line.StartsWith(label, StringComparison.OrdinalIgnoreCase))
                return false;

            value = line.Substring(label.Length).Trim();
            return !string.IsNullOrWhiteSpace(value);
        }

        private static bool LooksLikeHeading(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return false;

            return line.EndsWith(':') &&
                   !line.Contains('\\') &&
                   !line.Contains('/');
        }

        private static bool LooksLikeFileReference(string line)
            => !string.IsNullOrWhiteSpace(line) && FileReferenceRegex.IsMatch(line);

        private static bool ContainsConstraintKeyword(string line)
        {
            for (int i = 0; i < ConstraintKeywords.Length; i++)
            {
                if (line.Contains(ConstraintKeywords[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static string StripBullet(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return string.Empty;

            var normalized = Normalize(line);
            if (normalized.StartsWith("- ", StringComparison.Ordinal))
                return normalized.Substring(2).Trim();

            if (NumberedListRegex.IsMatch(normalized))
                return NumberedListRegex.Replace(normalized, string.Empty).Trim();

            return normalized;
        }

        private static void AddRange(List<string> target, List<string> values)
        {
            if (target == null || values == null)
                return;

            foreach (var value in values)
            {
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                target.Add(value);
                if (target.Count >= Filter2Rules.DeterministicFactMaxItems)
                    return;
            }
        }

        private static int InferFrozenBoundaryLineIndex(IReadOnlyList<Filter1BuildSeed.SeedExchange> exchanges)
        {
            var max = -1;
            if (exchanges == null)
                return max;

            foreach (var exchange in exchanges)
            {
                if (exchange == null)
                    continue;

                if (exchange.UserLineIndex > max)
                    max = exchange.UserLineIndex;

                if (exchange.AssistantLineIndex > max)
                    max = exchange.AssistantLineIndex;
            }

            return max;
        }
    }
}
