using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using VAL.Continuum.Pipeline.Filter1;

namespace VAL.Continuum.Pipeline.Filter2
{
    /// <summary>
    /// Filter 2: packs Seed exchanges into a single RestructuredSeed text blob.
    ///
    /// Output shape:
    /// - WHERE WE LEFT OFF — LAST COMPLETE EXCHANGE (AUTHORITATIVE): last complete exchange
    /// - HOW TO PROCEED
    /// - ACTIVE THREAD (MOST RELEVANT PRIOR EXCHANGE): remaining pinned exchanges, most recent first
    /// - CONTEXT FILLER (REFERENCE ONLY — DO NOT ADVANCE FROM HERE): older exchanges in reverse order (newest -> oldest), budgeted to ~28k chars
    /// </summary>
    public static class Filter2Restructure
    {
        private static readonly string[] HowToProceedLines =
        {
            "Proceed from \"WHERE WE LEFT OFF\".",
            "First assistant reply after injection: if WWLO is already answered, acknowledge readiness in one short line and wait.",
            "If WWLO contains a direct instruction, answer it.",
            "Do not restate, quote, or announce WWLO; answer it directly.",
            "Otherwise acknowledge continuity and wait."
        };
        private static readonly string[] ParagraphSeparators = { "\n\n" };

        public static string BuildRestructuredSeed(IReadOnlyList<Filter1BuildSeed.SeedExchange> exchanges)
        {
            if (exchanges == null || exchanges.Count == 0)
                return string.Empty;

            int total = exchanges.Count;
            int pin = Math.Min(Filter2Rules.WhereWeLeftOffCount, total);

            var pinnedTail = exchanges.Skip(total - pin).ToList();
            var lastExchange = pinnedTail.Count > 0 ? pinnedTail[pinnedTail.Count - 1] : null;

            var sb = new StringBuilder();

            var wwloBody = lastExchange != null
                ? FormatExchangeWhereWeLeftOff(lastExchange)
                : string.Empty;
            AppendSection(sb, "WHERE WE LEFT OFF — LAST COMPLETE EXCHANGE (AUTHORITATIVE)", wwloBody);

            var howToProceedBody = string.Join("\n", HowToProceedLines);
            AppendSection(sb, "HOW TO PROCEED", howToProceedBody);

            var activeThreadBody = new StringBuilder();
            // Render the remaining pinned tail (most recent first), excluding the last exchange used above.
            for (int i = pinnedTail.Count - 2; i >= 0; i--)
            {
                if (activeThreadBody.Length > 0)
                {
                    activeThreadBody.Append("\n\n");
                }
                activeThreadBody.Append(FormatExchange(pinnedTail[i], sanitizeAssistant: false));
            }
            AppendSection(sb, "ACTIVE THREAD (MOST RELEVANT PRIOR EXCHANGE)", activeThreadBody.ToString());

            // Reference-only context; downstream logic should not treat this section as authoritative.
            var fillerTitle = "CONTEXT FILLER (REFERENCE ONLY — DO NOT ADVANCE FROM HERE)";
            var fillerBody = new StringBuilder();
            int budget = Filter2Rules.BudgetChars;
            int overflowLimit = Filter2Rules.OverflowFinishExchangeMaxChars;
            int used = sb.Length + GetSectionHeaderLength(sb, fillerTitle) + GetSectionFooterLength();
            bool addedFiller = false;

            // Start adding older exchanges newest -> oldest (excluding the pinned tail).
            for (int i = total - pin - 1; i >= 0; i--)
            {
                var block = FormatExchange(exchanges[i], sanitizeAssistant: false);
                var prefix = fillerBody.Length > 0 ? "\n\n" : string.Empty;
                var blockWithGap = prefix + block;

                if (used + blockWithGap.Length <= budget)
                {
                    fillerBody.Append(blockWithGap);
                    used += blockWithGap.Length;
                    addedFiller = true;
                    continue;
                }

                // If we haven't crossed budget yet, allow ONE whole exchange as overflow.
                if (used < budget)
                {
                    if (blockWithGap.Length <= overflowLimit)
                    {
                        fillerBody.Append(blockWithGap);
                        used += blockWithGap.Length;
                        addedFiller = true;
                    }
                }

                break;
            }

            if (!addedFiller)
            {
                fillerBody.Append("(no reference-only exchanges captured)");
            }

            AppendSection(sb, fillerTitle, fillerBody.ToString());

            return sb.ToString();
        }

        
        private static string FormatExchangeWhereWeLeftOff(Filter1BuildSeed.SeedExchange ex)
        {
            var sb = new StringBuilder();

            sb.AppendLine(FormatSourceLine(ex));
            var user = SelectWwloText(ex.UserTextUncut, ex.UserText);
            var userOut = !string.IsNullOrWhiteSpace(user) ? user.Trim() : "[USER: empty]";
            sb.AppendLine($"USER: {userOut}");

            var assistant = SelectWwloText(ex.AssistantTextUncut, ex.AssistantText);
            AppendAssistantBlock(sb, assistant, sanitizeAssistant: false);
            return sb.ToString().TrimEnd();
        }

        private static string SelectWwloText(string uncut, string sliced)
        {
            var candidate = uncut ?? string.Empty;
            if (string.IsNullOrWhiteSpace(candidate))
                return sliced ?? string.Empty;

            const int MaxWwloUncutChars = Filter2Rules.WhereWeLeftOffMaxTextChars;
            return candidate.Length <= MaxWwloUncutChars ? candidate : (sliced ?? string.Empty);
        }

        private static string SanitizeAssistantForWwlo(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text ?? string.Empty;

            // If the assistant explicitly anchored a state sentence, do not strip those lines.
            bool HasAnchorTag(string line)
            {
                var t = line.Trim();
                return t.EndsWith("(goal)", StringComparison.OrdinalIgnoreCase)
                    || t.EndsWith("(checkpoint)", StringComparison.OrdinalIgnoreCase)
                    || t.EndsWith("(milestone)", StringComparison.OrdinalIgnoreCase);
            }

            var lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            int listy = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                var t = lines[i].TrimStart();
                if (HasAnchorTag(lines[i])) continue;

                if (t.StartsWith('-') ||
                    t.StartsWith('*') ||
                    t.StartsWith('•') ||
                    Regex.IsMatch(t, @"^\d+[\.\)]\s+"))
                {
                    listy++;
                }
            }

            // Also detect common procedural prompts that should not lead a handoff.
            int procedural = 0;
            foreach (var l in lines)
            {
                var t = l.Trim();
                if (HasAnchorTag(t)) continue;

                if (t.Contains("do these", StringComparison.OrdinalIgnoreCase) ||
                    t.Contains("checks in order", StringComparison.OrdinalIgnoreCase) ||
                    t.Contains("tell me which", StringComparison.OrdinalIgnoreCase) ||
                    t.Contains("report back", StringComparison.OrdinalIgnoreCase) ||
                    t.Contains("answer just this", StringComparison.OrdinalIgnoreCase))
                {
                    procedural++;
                }
            }

            bool looksLikeChecklist = listy >= 3 || procedural >= 2;

            if (!looksLikeChecklist)
            {
                return text.Trim();
            }

            // Remove list blocks (numbered/bulleted) while preserving anchored lines.
            var kept = new List<string>(lines.Length);
            for (int i = 0; i < lines.Length; i++)
            {
                var raw = lines[i];
                var t = raw.TrimStart();

                if (HasAnchorTag(raw))
                {
                    kept.Add(raw);
                    continue;
                }

                bool isListy = t.StartsWith('-') ||
                               t.StartsWith('*') ||
                               t.StartsWith('•') ||
                               Regex.IsMatch(t, @"^\d+[\.\)]\s+");

                if (isListy) continue;

                // Drop common "ops prompt" lines that are only relevant at runtime.
                var tt = raw.Trim();
                if (tt.Contains("do these", StringComparison.OrdinalIgnoreCase) ||
                    tt.Contains("checks in order", StringComparison.OrdinalIgnoreCase) ||
                    tt.Contains("tell me which", StringComparison.OrdinalIgnoreCase) ||
                    tt.Contains("report back", StringComparison.OrdinalIgnoreCase) ||
                    tt.Contains("answer just this", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                kept.Add(raw);
            }

            // Collapse excessive blank lines
            var collapsed = new List<string>(kept.Count);
            bool lastBlank = false;
            foreach (var l in kept)
            {
                bool blank = string.IsNullOrWhiteSpace(l);
                if (blank)
                {
                    if (!lastBlank) collapsed.Add(string.Empty);
                    lastBlank = true;
                }
                else
                {
                    collapsed.Add(l.TrimEnd());
                    lastBlank = false;
                }
            }

            var result = string.Join("\n", collapsed).Trim();

            // If we stripped too much, fall back to a conservative head+tail extraction.
            if (result.Length < 40 && text.Length > 80)
            {
                // Keep first 2 paragraphs and last paragraph.
                var paras = text.Split(ParagraphSeparators, StringSplitOptions.None)
                    .Select(p => p.Trim())
                    .Where(p => p.Length > 0)
                    .ToList();
                if (paras.Count <= 3) return text.Trim();

                var take = new List<string>();
                take.Add(paras[0]);
                take.Add(paras[1]);
                take.Add(paras[paras.Count - 1]);
                result = string.Join("\n\n", take).Trim();
            }

            return result;
        }

        private static string FormatExchange(Filter1BuildSeed.SeedExchange ex, bool sanitizeAssistant)
        {
            var sb = new StringBuilder();

            sb.AppendLine(FormatSourceLine(ex));
            var assistant = !string.IsNullOrWhiteSpace(ex.AssistantText) ? ex.AssistantText.Trim() : string.Empty;
            var user = !string.IsNullOrWhiteSpace(ex.UserText) ? ex.UserText.Trim() : "[USER: empty]";
            sb.AppendLine(FormattableString.Invariant($"USER: {user}"));
            AppendAssistantBlock(sb, assistant, sanitizeAssistant);

            return sb.ToString().TrimEnd();
        }

        private static string FormatSourceLine(Filter1BuildSeed.SeedExchange ex)
            => FormattableString.Invariant($"Source: Truth {FormatTruthRange(ex.UserLineIndex, ex.AssistantLineIndex)}");

        private static void AppendSection(StringBuilder sb, string title, string bodyText)
        {
            AppendHeading(sb, title, sb);

            if (!string.IsNullOrEmpty(bodyText))
            {
                sb.Append(bodyText.TrimEnd('\r', '\n'));
                sb.AppendLine();
            }
        }

        private static int GetSectionHeaderLength(StringBuilder sb, string title)
        {
            var header = new StringBuilder();
            AppendHeading(header, title, sb);
            return header.Length;
        }

        private static int GetSectionFooterLength()
            => Environment.NewLine.Length;

        private static void AppendHeading(StringBuilder target, string title, StringBuilder context)
    {
        // Markdown-first headings: render cleanly in ChatGPT after ProseMirror injection,
        // while remaining readable as plain text on disk.
        // IMPORTANT: keep the HR line isolated with blank lines so Markdown parses reliably.
        target.AppendLine();
        target.AppendLine("---");
        target.AppendLine();
        target.Append("## ").AppendLine(title);
        target.AppendLine();
    }

        private static void EnsureHeadingGap(StringBuilder target, StringBuilder context)
        {
            if (context.Length == 0)
            {
                target.AppendLine();
                return;
            }

            int trailingBreaks = CountTrailingLineBreaks(context);
            if (ReferenceEquals(target, context) && trailingBreaks > 2)
            {
                TrimTrailingLineBreaks(target, 2);
                trailingBreaks = 2;
            }
            else if (trailingBreaks > 2)
            {
                trailingBreaks = 2;
            }

            if (trailingBreaks == 0)
            {
                target.AppendLine();
                target.AppendLine();
                return;
            }

            if (trailingBreaks == 1)
            {
                target.AppendLine();
            }
        }

        private static int CountTrailingLineBreaks(StringBuilder sb)
        {
            int count = 0;
            int index = sb.Length - 1;
            while (index >= 0)
            {
                char ch = sb[index];
                if (ch == '\n')
                {
                    count++;
                    index--;
                    if (index >= 0 && sb[index] == '\r')
                    {
                        index--;
                    }
                    continue;
                }

                if (ch == '\r')
                {
                    count++;
                    index--;
                    continue;
                }

                break;
            }

            return count;
        }

        private static void TrimTrailingLineBreaks(StringBuilder sb, int keepCount)
        {
            int trailing = CountTrailingLineBreaks(sb);
            while (trailing > keepCount && sb.Length > 0)
            {
                char ch = sb[sb.Length - 1];
                if (ch == '\n')
                {
                    sb.Length -= 1;
                    if (sb.Length > 0 && sb[sb.Length - 1] == '\r')
                    {
                        sb.Length -= 1;
                    }
                }
                else if (ch == '\r')
                {
                    sb.Length -= 1;
                }

                trailing--;
            }
        }

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
            {
                return "?–?";
            }

            return $"{min}\u2013{max}";
        }

        private static void AppendAssistantBlock(StringBuilder sb, string assistantText, bool sanitizeAssistant)
        {
            sb.AppendLine("ASSISTANT:");
            sb.AppendLine(NormalizeAssistantContent(assistantText, sanitizeAssistant));
        }

        private static string NormalizeAssistantContent(string assistantText, bool sanitizeAssistant)
        {
            var raw = assistantText ?? string.Empty;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return "[ASSISTANT: empty]";
            }

            var match = Regex.Match(raw, @"^\s*Reasoned for (?:\d+h )?\d+m \d+s(?:\r?\n)?", RegexOptions.CultureInvariant);
            var remainder = match.Success ? raw.Substring(match.Length) : raw;

            var content = remainder;
            if (sanitizeAssistant)
            {
                content = SanitizeAssistantForWwlo(content);
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                return "[ASSISTANT: empty]";
            }

            return content;
        }
    }
}
