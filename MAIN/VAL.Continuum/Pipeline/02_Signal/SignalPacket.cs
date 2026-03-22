using System;
using System.Collections.Generic;
using System.Text;

namespace VAL.Continuum.Pipeline.Signal
{
    internal static class SignalPacket
    {
        internal const string PreviousChatSummaryHeading = "PREVIOUS CHAT SUMMARY";
        internal const string OpenLoopsHeading = "OPEN LOOPS";
        internal const string CriticalContextHeading = "CRITICAL CONTEXT";

        private static readonly string[] SignalHeadings =
        {
            PreviousChatSummaryHeading,
            OpenLoopsHeading,
            CriticalContextHeading
        };

        private static readonly HashSet<string> ForbiddenHeadings = new(StringComparer.Ordinal)
        {
            "VAL Pulse Handoff",
            "End of Pulse Handoff",
            "PRIME DIRECTIVE (READ FIRST)",
            "CURRENT STATE",
            "TAIL CHECK",
            "WHERE WE LEFT OFF — LAST COMPLETE EXCHANGE (AUTHORITATIVE)",
            "WHERE WE LEFT OFF",
            "HOW TO PROCEED",
            "ARTIFACTS AND REFERENCES",
            "ACTIVE THREAD (MOST RELEVANT PRIOR EXCHANGE)",
            "CONTEXT FILLER (REFERENCE ONLY — DO NOT ADVANCE FROM HERE)",
            "TRUTH WALKBACK HIGHLIGHTS"
        };

        internal static bool TryParse(string text, out SignalSummary summary)
        {
            summary = null!;

            var normalized = Normalize(text).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
                return false;

            if (!TrySplitSections(normalized, SignalHeadings, out var prefix, out var sections))
                return false;

            if (!string.IsNullOrWhiteSpace(prefix))
                return false;

            if (ContainsForbiddenHeading(normalized))
                return false;

            if (!TryParseBulletSection(GetSection(sections, PreviousChatSummaryHeading), out var previousChatSummary))
                return false;

            if (!TryParseBulletSection(GetSection(sections, OpenLoopsHeading), out var openLoops))
                return false;

            if (!TryParseBulletSection(GetSection(sections, CriticalContextHeading), out var criticalContext))
                return false;

            summary = new SignalSummary
            {
                PreviousChatSummary = previousChatSummary,
                OpenLoops = openLoops,
                CriticalContext = criticalContext
            };

            return true;
        }

        private static bool TryParseBulletSection(string body, out IReadOnlyList<string> bullets)
        {
            bullets = Array.Empty<string>();

            var normalized = Normalize(body);
            if (string.IsNullOrWhiteSpace(normalized))
                return false;

            var lines = normalized.Split('\n');
            var items = new List<string>();
            StringBuilder? current = null;

            for (int i = 0; i < lines.Length; i++)
            {
                var rawLine = lines[i].TrimEnd();
                if (string.IsNullOrWhiteSpace(rawLine))
                    continue;

                var trimmed = rawLine.TrimStart();
                if (trimmed.StartsWith("- ", StringComparison.Ordinal))
                {
                    FinalizeCurrent(items, current);
                    current = new StringBuilder(trimmed.Substring(2).Trim());
                    continue;
                }

                if (current == null)
                    return false;

                if (ForbiddenHeadings.Contains(trimmed))
                    return false;

                current.Append(' ').Append(trimmed);
            }

            FinalizeCurrent(items, current);
            if (items.Count == 0)
                return false;

            bullets = items;
            return true;
        }

        private static void FinalizeCurrent(ICollection<string> bullets, StringBuilder? current)
        {
            if (current == null)
                return;

            var value = current.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(value))
                bullets.Add(value);
        }

        private static bool TrySplitSections(
            string text,
            IReadOnlyList<string> headings,
            out string prefix,
            out Dictionary<string, string> sections)
        {
            prefix = string.Empty;
            sections = new Dictionary<string, string>(StringComparer.Ordinal);

            var normalized = Normalize(text).Trim('\n');
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

            prefix = Normalize(string.Join("\n", lines, 0, headingIndexes[0]));

            for (int i = 0; i < headings.Count; i++)
            {
                int start = headingIndexes[i] + 1;
                int endExclusive = (i + 1 < headingIndexes.Count) ? headingIndexes[i + 1] : lines.Length;
                var body = string.Join("\n", lines, start, endExclusive - start);
                sections[headings[i]] = Normalize(body);
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

        private static bool ContainsForbiddenHeading(string text)
        {
            var lines = Normalize(text).Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (ForbiddenHeadings.Contains(lines[i].Trim()))
                    return true;
            }

            return false;
        }

        private static string GetSection(IReadOnlyDictionary<string, string> sections, string heading)
            => sections.TryGetValue(heading, out var body) ? body : string.Empty;

        private static string Normalize(string text)
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
    }
}
