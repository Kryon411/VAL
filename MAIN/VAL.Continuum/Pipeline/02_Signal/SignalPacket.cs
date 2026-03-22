using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace VAL.Continuum.Pipeline.Signal
{
    internal static class SignalPacket
    {
        internal const string SignalTitle = "VAL Pulse Handoff";
        internal const string SignalFooter = "End of Pulse Handoff";
        internal const string PrimeDirectiveHeading = "PRIME DIRECTIVE (READ FIRST)";
        internal const string CurrentStateHeading = "CURRENT STATE";
        internal const string TailCheckHeading = "TAIL CHECK";
        internal const string WhereWeLeftOffHeading = "WHERE WE LEFT OFF — LAST COMPLETE EXCHANGE (AUTHORITATIVE)";
        internal const string HowToProceedHeading = "HOW TO PROCEED";
        internal const string OpenLoopsHeading = "OPEN LOOPS";
        internal const string CriticalContextHeading = "CRITICAL CONTEXT";
        internal const string ArtifactsHeading = "ARTIFACTS AND REFERENCES";
        internal const string ActiveThreadHeading = "ACTIVE THREAD (MOST RELEVANT PRIOR EXCHANGE)";
        internal const string ContextFillerHeading = "CONTEXT FILLER (REFERENCE ONLY — DO NOT ADVANCE FROM HERE)";

        private static readonly string[] SignalHeadings =
        {
            CurrentStateHeading,
            TailCheckHeading,
            WhereWeLeftOffHeading,
            HowToProceedHeading,
            OpenLoopsHeading,
            CriticalContextHeading,
            ArtifactsHeading,
            ActiveThreadHeading,
            ContextFillerHeading
        };

        private static readonly string[] TemplateHeadings =
        {
            PrimeDirectiveHeading,
            CurrentStateHeading,
            TailCheckHeading,
            WhereWeLeftOffHeading,
            HowToProceedHeading,
            OpenLoopsHeading,
            CriticalContextHeading,
            ArtifactsHeading,
            ActiveThreadHeading,
            ContextFillerHeading
        };

        private static readonly string[] CurrentStateFields =
        {
            "Status:",
            "Thread mode:",
            "Active objective:",
            "Next expected assistant action:",
            "Last stable checkpoint:"
        };

        internal sealed class ParsedPacket
        {
            public string CurrentState { get; init; } = string.Empty;
            public string TailCheck { get; init; } = string.Empty;
            public ExchangeBlock WhereWeLeftOff { get; init; } = ExchangeBlock.Empty;
            public string OpenLoops { get; init; } = string.Empty;
            public string CriticalContext { get; init; } = string.Empty;
            public string ArtifactsAndReferences { get; init; } = string.Empty;
            public string ActiveThread { get; init; } = string.Empty;
            public string ContextFiller { get; init; } = string.Empty;
        }

        internal sealed class ExchangeBlock
        {
            public static ExchangeBlock Empty { get; } = new();

            public string Source { get; init; } = string.Empty;
            public string User { get; init; } = string.Empty;
            public string Assistant { get; init; } = string.Empty;
        }

        private sealed class TemplateLayout
        {
            public string Prefix { get; init; } = string.Empty;
            public IReadOnlyDictionary<string, string> Sections { get; init; } = new Dictionary<string, string>();
        }

        internal static bool TryParse(string text, out ParsedPacket packet)
        {
            packet = null!;

            var normalized = NormalizeNewlines(text).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
                return false;

            var lines = normalized.Split('\n');
            if (lines.Length < 3)
                return false;

            int first = FindFirstNonEmptyLine(lines, 0);
            int last = FindLastNonEmptyLine(lines);
            if (first < 0 || last <= first)
                return false;

            if (!string.Equals(lines[first].Trim(), SignalTitle, StringComparison.Ordinal))
                return false;

            if (!string.Equals(lines[last].Trim(), SignalFooter, StringComparison.Ordinal))
                return false;

            var body = string.Join("\n", lines.Skip(first + 1).Take(last - first - 1));
            if (!TrySplitSections(body, SignalHeadings, out var _, out var sections))
                return false;

            var currentState = NormalizeSectionBody(GetSection(sections, CurrentStateHeading));
            if (!ValidateCurrentState(currentState))
                return false;

            var whereWeLeftOffBody = NormalizeSectionBody(GetSection(sections, WhereWeLeftOffHeading));
            if (string.IsNullOrWhiteSpace(whereWeLeftOffBody))
                return false;

            if (!HasRequiredStandaloneAnchor(whereWeLeftOffBody, "USER:") ||
                !HasRequiredStandaloneAnchor(whereWeLeftOffBody, "ASSISTANT:"))
            {
                return false;
            }

            if (!TryParseExchange(whereWeLeftOffBody, requireAnchors: true, out var whereWeLeftOff))
                return false;

            packet = new ParsedPacket
            {
                CurrentState = currentState,
                TailCheck = NormalizeSectionBody(GetSection(sections, TailCheckHeading)),
                WhereWeLeftOff = whereWeLeftOff,
                OpenLoops = NormalizeSectionBody(GetSection(sections, OpenLoopsHeading)),
                CriticalContext = NormalizeSectionBody(GetSection(sections, CriticalContextHeading)),
                ArtifactsAndReferences = NormalizeSectionBody(GetSection(sections, ArtifactsHeading)),
                ActiveThread = NormalizeSectionBody(GetSection(sections, ActiveThreadHeading)),
                ContextFiller = NormalizeSectionBody(GetSection(sections, ContextFillerHeading))
            };

            return true;
        }

        internal static bool TryRenderPulsePacket(string templateText, ParsedPacket packet, out string rendered)
        {
            rendered = string.Empty;

            if (packet == null)
                return false;

            if (!TryParseTemplate(templateText, out var layout))
                return false;

            var sections = new List<(string Heading, string Body)>
            {
                (PrimeDirectiveHeading, GetSection(layout.Sections, PrimeDirectiveHeading)),
                (CurrentStateHeading, packet.CurrentState),
                (TailCheckHeading, packet.TailCheck),
                (WhereWeLeftOffHeading, RenderExchange(packet.WhereWeLeftOff, compactAssistant: true)),
                (HowToProceedHeading, GetSection(layout.Sections, HowToProceedHeading)),
                (OpenLoopsHeading, packet.OpenLoops),
                (CriticalContextHeading, packet.CriticalContext),
                (ArtifactsHeading, packet.ArtifactsAndReferences),
                (ActiveThreadHeading, packet.ActiveThread),
                (ContextFillerHeading, packet.ContextFiller)
            };

            var sb = new StringBuilder();
            AppendBlock(sb, NormalizeSectionBody(layout.Prefix));

            foreach (var section in sections)
            {
                AppendSection(sb, section.Heading, section.Body);
            }

            rendered = sb.ToString().Trim();
            return !string.IsNullOrWhiteSpace(rendered);
        }

        private static bool TryParseTemplate(string templateText, out TemplateLayout layout)
        {
            layout = null!;

            var normalized = NormalizeNewlines(templateText).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
                return false;

            if (!TrySplitSections(normalized, TemplateHeadings, out var prefix, out var sections))
                return false;

            var primeDirective = NormalizeSectionBody(GetSection(sections, PrimeDirectiveHeading));
            var howToProceed = NormalizeSectionBody(GetSection(sections, HowToProceedHeading));

            if (string.IsNullOrWhiteSpace(prefix) ||
                string.IsNullOrWhiteSpace(primeDirective) ||
                string.IsNullOrWhiteSpace(howToProceed))
            {
                return false;
            }

            layout = new TemplateLayout
            {
                Prefix = prefix,
                Sections = sections
            };

            return true;
        }

        private static bool TrySplitSections(string text, IReadOnlyList<string> headings, out string prefix, out Dictionary<string, string> sections)
        {
            prefix = string.Empty;
            sections = new Dictionary<string, string>(StringComparer.Ordinal);

            var normalized = NormalizeNewlines(text).Trim('\n');
            var lines = normalized.Split('\n');
            if (lines.Length == 0)
                return false;

            var headingIndexes = new List<int>(headings.Count);
            int searchStart = 0;

            foreach (var heading in headings)
            {
                int found = FindHeadingLine(lines, heading, searchStart);
                if (found < 0)
                    return false;

                headingIndexes.Add(found);
                searchStart = found + 1;
            }

            prefix = NormalizeSectionBody(string.Join("\n", lines.Take(headingIndexes[0])));

            for (int i = 0; i < headings.Count; i++)
            {
                int start = headingIndexes[i] + 1;
                int endExclusive = (i + 1 < headingIndexes.Count) ? headingIndexes[i + 1] : lines.Length;
                var body = string.Join("\n", lines.Skip(start).Take(endExclusive - start));
                sections[headings[i]] = NormalizeSectionBody(body);
            }

            return true;
        }

        private static int FindHeadingLine(string[] lines, string heading, int start)
        {
            for (int i = start; i < lines.Length; i++)
            {
                if (string.Equals(lines[i].Trim(), heading, StringComparison.Ordinal))
                    return i;
            }

            return -1;
        }

        private static bool ValidateCurrentState(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return false;

            var lines = NormalizeSectionBody(body).Split('\n');
            int index = 0;

            foreach (var field in CurrentStateFields)
            {
                while (index < lines.Length && string.IsNullOrWhiteSpace(lines[index]))
                    index++;

                if (index >= lines.Length)
                    return false;

                var line = lines[index];
                if (!line.StartsWith(field, StringComparison.Ordinal))
                    return false;

                var value = line.Substring(field.Length).Trim();
                if (string.IsNullOrWhiteSpace(value))
                    return false;

                index++;
            }

            return true;
        }

        private static bool TryParseExchange(string body, bool requireAnchors, out ExchangeBlock exchange)
        {
            exchange = ExchangeBlock.Empty;

            var normalized = NormalizeSectionBody(body);
            if (string.IsNullOrWhiteSpace(normalized))
                return false;

            var lines = normalized.Split('\n');
            int sourceIndex = FindSourceLine(lines);
            int userIndex = FindStandaloneLabelLine(lines, "USER:");
            int assistantIndex = FindStandaloneLabelLine(lines, "ASSISTANT:");

            if (sourceIndex < 0 || userIndex < 0 || assistantIndex < 0)
                return false;

            if (!(sourceIndex < userIndex && userIndex < assistantIndex))
                return false;

            var source = ReadLabeledBlock(lines, sourceIndex, userIndex, "Source:");
            var user = ReadLabeledBlock(lines, userIndex, assistantIndex, "USER:");
            var assistant = ReadLabeledBlock(lines, assistantIndex, lines.Length, "ASSISTANT:");

            if (requireAnchors && (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(assistant)))
                return false;

            exchange = new ExchangeBlock
            {
                Source = source,
                User = user,
                Assistant = assistant
            };

            return true;
        }

        private static int FindSourceLine(string[] lines)
        {
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].StartsWith("Source:", StringComparison.Ordinal))
                    return i;
            }

            return -1;
        }

        private static int FindStandaloneLabelLine(string[] lines, string label)
        {
            for (int i = 0; i < lines.Length; i++)
            {
                if (string.Equals(lines[i].Trim(), label, StringComparison.Ordinal))
                    return i;
            }

            return -1;
        }

        private static bool HasRequiredStandaloneAnchor(string body, string label)
        {
            if (string.IsNullOrWhiteSpace(body))
                return false;

            var lines = NormalizeSectionBody(body).Split('\n');
            return FindStandaloneLabelLine(lines, label) >= 0;
        }

        private static string ReadLabeledBlock(string[] lines, int labelIndex, int nextLabelIndex, string label)
        {
            var sb = new StringBuilder();
            var firstLine = lines[labelIndex].Substring(label.Length).Trim();
            if (!string.IsNullOrWhiteSpace(firstLine))
                sb.AppendLine(firstLine);

            for (int i = labelIndex + 1; i < nextLabelIndex; i++)
            {
                sb.AppendLine(lines[i]);
            }

            return NormalizeSectionBody(sb.ToString());
        }

        private static void AppendSection(StringBuilder sb, string heading, string body)
        {
            if (sb.Length > 0)
            {
                sb.AppendLine();
                sb.AppendLine();
            }

            sb.AppendLine(heading);
            var normalizedBody = NormalizeSectionBody(body);
            if (!string.IsNullOrWhiteSpace(normalizedBody))
                sb.Append(normalizedBody);
        }

        private static void AppendBlock(StringBuilder sb, string block)
        {
            var normalized = NormalizeSectionBody(block);
            if (string.IsNullOrWhiteSpace(normalized))
                return;

            if (sb.Length > 0)
            {
                sb.AppendLine();
                sb.AppendLine();
            }

            sb.Append(normalized);
        }

        private static string RenderExchange(ExchangeBlock exchange, bool compactAssistant)
        {
            var sb = new StringBuilder();
            sb.Append("Source:");
            if (!string.IsNullOrWhiteSpace(exchange.Source))
            {
                sb.Append(' ');
                sb.AppendLine(exchange.Source.Trim());
            }
            else
            {
                sb.AppendLine();
            }

            sb.AppendLine("USER:");
            sb.AppendLine(NormalizeSectionBody(exchange.User));
            sb.AppendLine("ASSISTANT:");
            sb.Append(CompactAssistantText(exchange.Assistant, compactAssistant));
            return sb.ToString().TrimEnd();
        }

        private static string CompactAssistantText(string assistant, bool compact)
        {
            var normalized = NormalizeSectionBody(assistant);
            if (!compact || string.IsNullOrWhiteSpace(normalized))
                return normalized;

            const int maxChars = 900;
            if (normalized.Length <= maxChars)
                return normalized;

            var paragraphs = Regex.Split(normalized, @"\n{2,}")
                .Select(NormalizeSectionBody)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList();

            var sb = new StringBuilder();
            foreach (var paragraph in paragraphs)
            {
                var candidate = sb.Length == 0 ? paragraph : sb.ToString() + "\n\n" + paragraph;
                if (candidate.Length > maxChars && sb.Length > 0)
                    break;

                if (candidate.Length > maxChars)
                {
                    sb.Append(paragraph.Substring(0, maxChars).Trim());
                    break;
                }

                if (sb.Length > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine();
                }

                sb.Append(paragraph);
            }

            var compacted = NormalizeSectionBody(sb.ToString());
            if (string.IsNullOrWhiteSpace(compacted))
                compacted = normalized.Substring(0, Math.Min(maxChars, normalized.Length)).Trim();

            if (compacted.Length < normalized.Length)
                compacted = compacted.TrimEnd() + " ...";

            return compacted;
        }

        private static string NormalizeSectionBody(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var lines = NormalizeNewlines(text).Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                lines[i] = lines[i].TrimEnd();
            }

            return string.Join("\n", lines).Trim();
        }

        private static string NormalizeNewlines(string text)
            => (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');

        private static int FindFirstNonEmptyLine(string[] lines, int start)
        {
            for (int i = start; i < lines.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(lines[i]))
                    return i;
            }

            return -1;
        }

        private static int FindLastNonEmptyLine(string[] lines)
        {
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                if (!string.IsNullOrWhiteSpace(lines[i]))
                    return i;
            }

            return -1;
        }

        private static string GetSection(IReadOnlyDictionary<string, string> sections, string heading)
        {
            return sections.TryGetValue(heading, out var body) ? body : string.Empty;
        }
    }
}
