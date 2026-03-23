using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace VAL.Continuum.Pipeline.Filter1
{
    /// <summary>
    /// Filter 1 rules (Truth.log -> Seed.log projection).
    ///
    /// Design intent:
    /// - Deterministic + boring (no semantic inference).
    /// - Prefer preserving continuity over preserving verbatim bulk.
    /// - Slice long assistant turns with head+tail extraction (~1.5k total), sentence-safe.
    /// </summary>
    internal static class Filter1Rules
    {
        // ---- Slice budgets (tune freely) ----
        // Typical assistant responses: ~4-6k chars. Target ~1.5k for handoff continuity.
        public const int AssistantSliceTotalChars = 1_500;
        public const int AssistantSliceSideChars = AssistantSliceTotalChars / 2; // 750

        // Extra room to complete sentences / avoid hard word cuts.
        public const int AssistantSentenceOverflowMaxChars = 420;

        // Users are usually short once uploads + dumps are stripped. If not, slice.
        public const int UserSliceMaxChars = 1_200;
        public const int UserSentenceOverflowMaxChars = 280;

        // Hard guardrail: if anything still explodes, don't let a single exchange dominate Filter 2's 28k pack.
        public const int MaxExchangeChars = 12_000;

        // ---- Noise stubs ----
        // Stub fenced code blocks (never include raw code in Seed.log by default).
        private static readonly Regex CodeFenceRegex = new(@"```.*?```", RegexOptions.Singleline | RegexOptions.Compiled);

        // Detect injected legacy seed blocks / prior packets that often get pasted into chat.
        private static readonly Regex LargeSeedPasteRegex =
            new(@"^\s*(Continuing\s+an\s+ongoing\s+working\s+session\s+with\s+VAL|CONTEXT\s+BLOCK\s+[—\-]\s+READ\s+ONLY|ESSENCE[\-\u2011]M\s+SNAPSHOT\s*\(AUTHORITATIVE\)|WHERE\s+WE\s+LEFT\s+OFF\s+[—\-]\s+LAST\s+COMPLETE\s+EXCHANGE\s*\(AUTHORITATIVE\)|CONTEXT\s+FILLER\s*\(REFERENCE\s+ONLY\s+[—\-]\s+DO\s+NOT\s+ADVANCE\s+FROM\s+HERE\))\b",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex EssenceHeaderRegex =
            new(@"^\s*(ESSENCE[\-\u2011]M\s+SNAPSHOT\s*\(AUTHORITATIVE\)|CONTEXT\s+BLOCK\s+[—\-]\s+READ\s+ONLY|WHERE\s+WE\s+LEFT\s+OFF\s+[—\-]\s+LAST\s+COMPLETE\s+EXCHANGE\s*\(AUTHORITATIVE\)|CONTEXT\s+FILLER\s*\(REFERENCE\s+ONLY\s+[—\-]\s+DO\s+NOT\s+ADVANCE\s+FROM\s+HERE\))\b",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // File name on its own line (upload cards often list "file.ext" then "Zip Archive"/"Document").
        private static readonly Regex FileNameLineRegex =
            new(@"^[\w \-\.]{1,120}\.(zip|png|jpg|jpeg|webp|pdf|txt|md|docx|pptx|xlsx|cs|js|json)$",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly HashSet<string> AttachmentDescriptorLines = new(StringComparer.OrdinalIgnoreCase)
        {
            "Zip Archive",
            "Document",
            "Image",
            "PNG image",
            "JPG image",
            "JPEG image",
            "WEBP image",
            "PDF",
            "PDF Document"
        };

        public static string FilterUser(string text) => FilterAndSlice(text, isAssistant: false, slice: true);
        public static string FilterAssistant(string text) => FilterAndSlice(text, isAssistant: true, slice: true);

        public static string FilterUserUncut(string text) => FilterAndSlice(text, isAssistant: false, slice: false);
        public static string FilterAssistantUncut(string text) => FilterAndSlice(text, isAssistant: true, slice: false);

        private static string FilterAndSlice(string text, bool isAssistant, bool slice)
        {
            var s = (text ?? string.Empty).Replace("\r\n", "\n").Trim();
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;

            // 0) Strip upload card prefixes (filename + descriptor lines).
            s = StripUploadCardPrefix(s);

            // 1) Stub giant legacy seed pastes (they are usually not needed in handoff history).
            if (LargeSeedPasteRegex.IsMatch(s))
                return "[PASTE OMITTED: Legacy handoff block]";

            if (EssenceHeaderRegex.IsMatch(s))
                return "[PASTE OMITTED: Prior Essence-M seed]";

            // 2) Stub code fences.
            s = StubCodeFences(s);

            // 3) Mechanical whitespace compaction.
            s = CollapseBlankLines(s);

            // 4) Collapse accidental repeated blocks (prevents "stutter" output).
            s = CollapseConsecutiveDuplicateParagraphs(s);

            if (!slice)
                return s;

            // 5) Slice if needed.
            if (isAssistant)
                return SliceAssistant(s);

            return SliceUser(s);
        }

        private static string StripUploadCardPrefix(string s)
        {
            // Many user turns start with:
            // "file.zip\nZip Archive\nother.txt\n..."
            // We drop leading filename/descriptor lines until we hit real prose.
            try
            {
                var lines = s.Split('\n');
                if (lines.Length <= 2) return s;

                int i = 0;
                int dropped = 0;
                while (i < lines.Length)
                {
                    var ln = (lines[i] ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(ln)) { i++; dropped++; continue; }

                    bool isFile = FileNameLineRegex.IsMatch(ln);
                    bool isDesc = AttachmentDescriptorLines.Contains(ln);

                    if (isFile || isDesc)
                    {
                        i++;
                        dropped++;
                        continue;
                    }

                    // Stop stripping when we encounter non-upload prose.
                    break;
                }

                if (dropped <= 0) return s;

                var rest = string.Join("\n", lines.Skip(i)).Trim();
                return string.IsNullOrWhiteSpace(rest) ? s : rest;
            }
            catch
            {
                return s;
            }
        }

        private static string StubCodeFences(string s)
        {
            try
            {
                return CodeFenceRegex.Replace(s, m =>
                {
                    var inner = m.Value ?? string.Empty;
                    int lines = inner.Count(c => c == '\n');
                    return $"[CODEBLOCK OMITTED ({Math.Max(lines, 1)} lines)]";
                });
            }
            catch
            {
                return s;
            }
        }

        private static string CollapseBlankLines(string s)
        {
            try
            {
                s = Regex.Replace(s, @"[ \t]+\n", "\n");
                s = Regex.Replace(s, @"\n{3,}", "\n\n");
                return s.Trim();
            }
            catch
            {
                return s.Trim();
            }
        }

        private static string CollapseConsecutiveDuplicateParagraphs(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;

            try
            {
                // Split into paragraphs. We treat 2+ newlines as a paragraph boundary.
                var parts = Regex.Split(s, @"\n{2,}");

                var paras = new List<string>();
                var keys = new List<string>();

                foreach (var raw in parts)
                {
                    var p = (raw ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(p))
                        continue;

                    paras.Add(p);
                    keys.Add(NormalizeParagraphKey(p));

                    // Remove adjacent repeated blocks (handles multi-paragraph "stutter" copies).
                    int maxBlock = Math.Min(12, paras.Count / 2);
                    for (int blockLen = maxBlock; blockLen >= 1; blockLen--)
                    {
                        bool match = true;
                        for (int i = 0; i < blockLen; i++)
                        {
                            var a = keys[keys.Count - 1 - i];
                            var b = keys[keys.Count - 1 - blockLen - i];
                            if (!string.Equals(a, b, StringComparison.Ordinal))
                            {
                                match = false;
                                break;
                            }
                        }

                        if (match)
                        {
                            paras.RemoveRange(paras.Count - blockLen, blockLen);
                            keys.RemoveRange(keys.Count - blockLen, blockLen);
                            break;
                        }
                    }
                }

                return string.Join("\n\n", paras).Trim();
            }
            catch
            {
                return s.Trim();
            }
        }

        private static string NormalizeParagraphKey(string p)
        {
            if (string.IsNullOrEmpty(p)) return string.Empty;

            // Collapse whitespace to single spaces so duplicates with slightly different newlines still match.
            var sb = new StringBuilder(Math.Min(p.Length, 512));
            bool inWs = false;

            for (int i = 0; i < p.Length; i++)
            {
                char c = p[i];
                if (char.IsWhiteSpace(c))
                {
                    if (!inWs)
                    {
                        sb.Append(' ');
                        inWs = true;
                    }
                }
                else
                {
                    sb.Append(c);
                    inWs = false;
                }

                if (sb.Length >= 512)
                    break;
            }

            return sb.ToString().Trim();
        }

        private static string SliceUser(string s)
        {
            if (s.Length <= UserSliceMaxChars)
                return s;

            int side = Math.Max(320, UserSliceMaxChars / 2);
            return SliceHeadTail(s, side, UserSentenceOverflowMaxChars);
        }

        private static string SliceAssistant(string s)
        {
            // If it's close to our target, keep it whole.
            int maxFull = AssistantSliceTotalChars + AssistantSentenceOverflowMaxChars;
            if (s.Length <= maxFull)
                return s;

            return SliceHeadTail(s, AssistantSliceSideChars, AssistantSentenceOverflowMaxChars);
        }

        private static string SliceHeadTail(string s, int sideChars, int overflow)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;

            s = s.Replace("\r\n", "\n");

            // If we're within a range where head+tail expansion would overlap, don't slice.
            // This prevents duplicated paragraphs caused by overlapping windows.
            int noSliceThreshold = sideChars * 2 + overflow * 2;
            if (s.Length <= noSliceThreshold)
                return s.Trim();

            var (headText, headEnd) = TakeHeadSentenceSafeWithIndex(s, sideChars, overflow);
            var (tailText, tailStart) = TakeTailSentenceSafeWithIndex(s, sideChars, overflow);

            if (string.IsNullOrWhiteSpace(headText)) return tailText?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(tailText)) return headText?.Trim() ?? string.Empty;

            // If the windows overlap (or touch), the union covers the whole message: keep it whole.
            if (headEnd >= tailStart)
                return s.Trim();

            var sb = new StringBuilder(headText.Length + tailText.Length + 16);
            sb.Append(headText.TrimEnd());
            if (!headText.TrimEnd().EndsWith("...", StringComparison.Ordinal))
                sb.Append("...");
            sb.AppendLine();
            sb.AppendLine();
            if (!tailText.TrimStart().StartsWith("...", StringComparison.Ordinal))
                sb.Append("...");
            sb.Append(tailText.TrimStart());

            return sb.ToString().Trim();
        }

        private static string SliceWindowSentenceSafe(string s, int left, int right, int overflow)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;

            left = Math.Max(0, left);
            right = Math.Min(s.Length, right);
            if (right <= left) return string.Empty;

            int newLeft = ExpandLeftToSentenceStart(s, left, overflow);
            int newRight = ExpandRightToSentenceEnd(s, right, overflow);

            newLeft = SnapStartToWordBoundary(s, newLeft);
            newRight = SnapEndToWordBoundary(s, newRight);

            if (newRight <= newLeft) return string.Empty;
            return s.Substring(newLeft, newRight - newLeft).Trim();
        }

        private static (string text, int endExclusive) TakeHeadSentenceSafeWithIndex(string s, int baseChars, int overflow)
        {
            int end = Math.Min(s.Length, baseChars);
            end = ExpandRightToSentenceEnd(s, end, overflow);
            end = SnapEndToWordBoundary(s, end);
            if (end <= 0) return (string.Empty, 0);
            return (s.Substring(0, end).Trim(), end);
        }

        private static (string text, int startInclusive) TakeTailSentenceSafeWithIndex(string s, int baseChars, int overflow)
        {
            int start = Math.Max(0, s.Length - baseChars);
            start = ExpandLeftToSentenceStart(s, start, overflow);
            start = SnapStartToWordBoundary(s, start);
            if (start >= s.Length) return (string.Empty, s.Length);
            return (s.Substring(start).Trim(), start);
        }

        private static int ExpandRightToSentenceEnd(string s, int end, int overflow)
        {
            int maxEnd = Math.Min(s.Length, end + overflow);
            for (int i = end; i < maxEnd; i++)
            {
                char c = s[i];
                if (c == '.' || c == '!' || c == '?' || c == '\n')
                    return i + 1;
            }
            return end;
        }

        private static int ExpandLeftToSentenceStart(string s, int start, int overflow)
        {
            int minStart = Math.Max(0, start - overflow);
            for (int i = start - 1; i >= minStart; i--)
            {
                char c = s[i];
                if (c == '.' || c == '!' || c == '?' || c == '\n')
                {
                    int j = i + 1;
                    while (j < s.Length && char.IsWhiteSpace(s[j]) && s[j] != '\n') j++;
                    return j;
                }
            }
            return start;
        }

        private static int SnapStartToWordBoundary(string s, int start)
        {
            if (start <= 0) return 0;
            if (start >= s.Length) return s.Length;

            // If we're in the middle of a word, move forward to whitespace.
            if (!char.IsWhiteSpace(s[start]) && !char.IsWhiteSpace(s[start - 1]))
            {
                int max = Math.Min(s.Length, start + 48);
                for (int i = start; i < max; i++)
                {
                    if (char.IsWhiteSpace(s[i]))
                        return Math.Min(s.Length, i + 1);
                }
            }

            return start;
        }

        private static int SnapEndToWordBoundary(string s, int end)
        {
            if (end <= 0) return 0;
            if (end >= s.Length) return s.Length;

            // If we're in the middle of a word, extend to whitespace.
            if (!char.IsWhiteSpace(s[end - 1]) && !char.IsWhiteSpace(s[end]))
            {
                int max = Math.Min(s.Length, end + 48);
                for (int i = end; i < max; i++)
                {
                    if (char.IsWhiteSpace(s[i]))
                        return i;
                }
            }

            return end;
        }
    }
}
