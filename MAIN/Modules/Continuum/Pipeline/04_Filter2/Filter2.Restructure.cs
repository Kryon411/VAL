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
        public static string BuildRestructuredSeed(IReadOnlyList<Filter1BuildSeed.SeedExchange> exchanges)
        {
            if (exchanges == null || exchanges.Count == 0)
                return string.Empty;

            int total = exchanges.Count;
            int pin = Math.Min(Filter2Rules.WhereWeLeftOffCount, total);

            var pinnedTail = exchanges.Skip(total - pin).ToList();
            var lastExchange = pinnedTail.Count > 0 ? pinnedTail[pinnedTail.Count - 1] : null;

            var sb = new StringBuilder();

            sb.AppendLine("WHERE WE LEFT OFF — LAST COMPLETE EXCHANGE (AUTHORITATIVE)");
            sb.AppendLine(Filter2Rules.SeparatorLine);
            sb.AppendLine();

            if (lastExchange != null)
            {
                sb.AppendLine(FormatExchangeWhereWeLeftOff(lastExchange));
                sb.AppendLine();
            }

            sb.AppendLine("HOW TO PROCEED");
            sb.AppendLine(Filter2Rules.SeparatorLine);
            sb.AppendLine();
            sb.AppendLine("Continue the same working session.");
            sb.AppendLine("Read WWLO first, then respond to the last user instruction.");
            sb.AppendLine();

            sb.AppendLine("ACTIVE THREAD (MOST RELEVANT PRIOR EXCHANGE)");
            sb.AppendLine(Filter2Rules.SeparatorLine);
            sb.AppendLine();

            // Render the remaining pinned tail (most recent first), excluding the last exchange used above.
            for (int i = pinnedTail.Count - 2; i >= 0; i--)
            {
                sb.AppendLine(FormatExchange(pinnedTail[i], sanitizeAssistant: false));
                sb.AppendLine();
            }

            // Reference-only context; downstream logic should not treat this section as authoritative.
            sb.AppendLine("CONTEXT FILLER (REFERENCE ONLY — DO NOT ADVANCE FROM HERE)");
            sb.AppendLine(Filter2Rules.SeparatorLine);
            sb.AppendLine();

            int budget = Filter2Rules.BudgetChars;
            int overflowLimit = Filter2Rules.OverflowFinishExchangeMaxChars;

            // Start adding older exchanges newest -> oldest (excluding the pinned tail).
            int used = sb.Length;
            for (int i = total - pin - 1; i >= 0; i--)
            {
                var block = FormatExchange(exchanges[i], sanitizeAssistant: false) + "\n\n";

                if (used + block.Length <= budget)
                {
                    sb.Append(block);
                    used += block.Length;
                    continue;
                }

                // If we haven't crossed budget yet, allow ONE whole exchange as overflow.
                if (used < budget)
                {
                    if (block.Length <= overflowLimit)
                    {
                        sb.Append(block);
                        used += block.Length;
                    }
                }

                break;
            }

            return sb.ToString().Trim();
        }

        
        private static string FormatExchangeWhereWeLeftOff(Filter1BuildSeed.SeedExchange ex)
        {
            var sb = new StringBuilder();

            sb.AppendLine(FormatSourceLine(ex));
            sb.AppendLine("USER:");
            var user = SelectWwloText(ex.UserTextUncut, ex.UserText);
            sb.AppendLine(!string.IsNullOrWhiteSpace(user) ? user.Trim() : "[USER: empty]");
            sb.AppendLine("ASSISTANT:");

            var assistant = SelectWwloText(ex.AssistantTextUncut, ex.AssistantText);
            sb.AppendLine(!string.IsNullOrWhiteSpace(assistant) ? assistant.Trim() : "[ASSISTANT: empty]");

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

                if (t.StartsWith("-", StringComparison.Ordinal) ||
                    t.StartsWith("*", StringComparison.Ordinal) ||
                    t.StartsWith("•", StringComparison.Ordinal) ||
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

                if (t.IndexOf("do these", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    t.IndexOf("checks in order", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    t.IndexOf("tell me which", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    t.IndexOf("report back", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    t.IndexOf("answer just this", StringComparison.OrdinalIgnoreCase) >= 0)
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

                bool isListy = t.StartsWith("-", StringComparison.Ordinal) ||
                               t.StartsWith("*", StringComparison.Ordinal) ||
                               t.StartsWith("•", StringComparison.Ordinal) ||
                               Regex.IsMatch(t, @"^\d+[\.\)]\s+");

                if (isListy) continue;

                // Drop common "ops prompt" lines that are only relevant at runtime.
                var tt = raw.Trim();
                if (tt.IndexOf("do these", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    tt.IndexOf("checks in order", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    tt.IndexOf("tell me which", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    tt.IndexOf("report back", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    tt.IndexOf("answer just this", StringComparison.OrdinalIgnoreCase) >= 0)
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
                var paras = text.Split(new[] { "\n\n" }, StringSplitOptions.None)
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
            sb.AppendLine("USER:");
            sb.AppendLine(!string.IsNullOrWhiteSpace(ex.UserText) ? ex.UserText.Trim() : "[USER: empty]");
            sb.AppendLine("ASSISTANT:");
            var assistant = !string.IsNullOrWhiteSpace(ex.AssistantText) ? ex.AssistantText.Trim() : "[ASSISTANT: empty]";
            sb.AppendLine(sanitizeAssistant ? SanitizeAssistantForWwlo(assistant) : assistant);

            return sb.ToString().TrimEnd();
        }

        private static string FormatSourceLine(Filter1BuildSeed.SeedExchange ex)
            => $"Source: Truth {FormatTruthRange(ex.UserLineIndex, ex.AssistantLineIndex)}";

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
    }
}
