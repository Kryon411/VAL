using System;
using System.Collections.Generic;
using System.Text;

namespace VAL.Continuum.Pipeline.Signal
{
    internal static class SignalPacket
    {
        internal const string PreviousChatSummaryHeading = "PREVIOUS CHAT SUMMARY";

        private static readonly HashSet<string> ForbiddenHeadings = new(StringComparer.Ordinal)
        {
            "CONTINUUM SIGNAL INPUT (EXCLUDE FROM CONTINUITY)",
            "VAL Pulse Handoff",
            "End of Pulse Handoff",
            "PRIME DIRECTIVE (READ FIRST)",
            "CURRENT STATE",
            "TAIL CHECK",
            "WHERE WE LEFT OFF — LAST COMPLETE EXCHANGE (AUTHORITATIVE)",
            "WHERE WE LEFT OFF",
            "HOW TO PROCEED",
            "OPEN LOOPS",
            "OPEN LOOP FACTS",
            "CRITICAL CONTEXT",
            "CRITICAL FACTS",
            "FROZEN PULSE SNAPSHOT",
            "DETERMINISTIC SOURCE MATERIAL",
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

            if (!TrySplitSingleSection(normalized, PreviousChatSummaryHeading, out var prefix, out var body))
                return false;

            if (!string.IsNullOrWhiteSpace(prefix))
                return false;

            if (ContainsForbiddenHeading(normalized))
                return false;

            if (!TryParseBulletSection(body, out var previousChatSummary))
                return false;

            summary = new SignalSummary
            {
                PreviousChatSummary = previousChatSummary
            };

            return true;
        }

        private static bool TryParseBulletSection(string body, out IReadOnlyList<string> bullets)
        {
            bullets = Array.Empty<string>();

            var normalized = Normalize(body);
            if (string.IsNullOrWhiteSpace(normalized))
                return false;

            if (TryParseMarkedBullets(normalized, out bullets))
                return true;

            if (TryParseParagraphBullets(normalized, out bullets))
                return true;

            bullets = Array.Empty<string>();
            return false;
        }

        private static bool TryParseMarkedBullets(string normalized, out IReadOnlyList<string> bullets)
        {
            bullets = Array.Empty<string>();

            var lines = normalized.Split('\n');
            var items = new List<string>();
            StringBuilder? current = null;
            var sawMarker = false;

            for (int i = 0; i < lines.Length; i++)
            {
                var rawLine = lines[i].TrimEnd();
                if (string.IsNullOrWhiteSpace(rawLine))
                    continue;

                var trimmed = rawLine.TrimStart();
                if (TryStripBulletMarker(trimmed, out var bulletText))
                {
                    sawMarker = true;
                    FinalizeCurrent(items, current);
                    current = new StringBuilder(bulletText);
                    continue;
                }

                if (current == null)
                    return false;

                if (ForbiddenHeadings.Contains(trimmed))
                    return false;

                current.Append(' ').Append(trimmed);
            }

            FinalizeCurrent(items, current);
            if (!sawMarker || items.Count == 0)
                return false;

            bullets = items;
            return true;
        }

        private static bool TryParseParagraphBullets(string normalized, out IReadOnlyList<string> bullets)
        {
            bullets = Array.Empty<string>();

            var lines = normalized.Split('\n');
            var items = new List<string>();
            var current = new StringBuilder();
            var paragraphCount = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    if (FinalizeParagraph(items, current))
                        paragraphCount++;

                    continue;
                }

                if (ForbiddenHeadings.Contains(trimmed))
                    return false;

                if (current.Length > 0)
                    current.Append(' ');

                current.Append(trimmed);
            }

            if (FinalizeParagraph(items, current))
                paragraphCount++;

            if (paragraphCount < 2 || items.Count == 0)
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

        private static bool FinalizeParagraph(ICollection<string> bullets, StringBuilder current)
        {
            if (current == null || current.Length == 0)
                return false;

            var value = current.ToString().Trim();
            current.Clear();

            if (string.IsNullOrWhiteSpace(value))
                return false;

            bullets.Add(value);
            return true;
        }

        private static bool TryStripBulletMarker(string text, out string value)
        {
            value = string.Empty;

            if (string.IsNullOrWhiteSpace(text))
                return false;

            if (text.StartsWith("- ", StringComparison.Ordinal) ||
                text.StartsWith("* ", StringComparison.Ordinal) ||
                text.StartsWith("• ", StringComparison.Ordinal))
            {
                value = text.Substring(2).Trim();
                return !string.IsNullOrWhiteSpace(value);
            }

            int markerLength = GetNumberedMarkerLength(text);
            if (markerLength <= 0)
                return false;

            value = text.Substring(markerLength).Trim();
            return !string.IsNullOrWhiteSpace(value);
        }

        private static int GetNumberedMarkerLength(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return 0;

            int i = 0;
            while (i < text.Length && char.IsDigit(text[i]))
                i++;

            if (i == 0 || i >= text.Length)
                return 0;

            if (text[i] != '.' && text[i] != ')')
                return 0;

            int markerLength = i + 1;
            if (markerLength < text.Length && text[markerLength] == ' ')
                markerLength++;

            return markerLength;
        }

        private static bool TrySplitSingleSection(
            string text,
            string heading,
            out string prefix,
            out string body)
        {
            prefix = string.Empty;
            body = string.Empty;

            var normalized = Normalize(text).Trim('\n');
            var lines = normalized.Split('\n');
            if (lines.Length == 0)
                return false;

            int headingIndex = FindHeadingLine(lines, heading, 0);
            if (headingIndex < 0)
                return false;

            prefix = Normalize(string.Join("\n", lines, 0, headingIndex));
            body = Normalize(string.Join("\n", lines, headingIndex + 1, lines.Length - headingIndex - 1));

            if (string.IsNullOrWhiteSpace(body))
                return false;

            for (int i = headingIndex + 1; i < lines.Length; i++)
            {
                var candidate = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(candidate))
                    continue;

                if (string.Equals(candidate, heading, StringComparison.Ordinal))
                    return false;
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
