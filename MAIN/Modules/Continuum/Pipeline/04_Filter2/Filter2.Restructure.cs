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
    /// - WHERE WE LEFT OFF — LAST COMPLETE EXCHANGE (authoritative): last complete exchange
    /// - HOW TO PROCEED
    /// - ACTIVE THREAD (most relevant prior exchange): remaining pinned exchanges, most recent first
    /// - CONTEXT FILLER (reference only): older exchanges in reverse order (newest -> oldest), budgeted to ~28k chars
    /// </summary>
    public static class Filter2Restructure
    {
        private const string SeparatorLine = "──────────────────────────────────────────────────";

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
            sb.AppendLine(SeparatorLine);
            sb.AppendLine();

            if (lastExchange != null)
            {
                sb.AppendLine(FormatExchangeWhereWeLeftOff(lastExchange));
                sb.AppendLine();
            }

            sb.AppendLine("HOW TO PROCEED");
            sb.AppendLine(SeparatorLine);
            sb.AppendLine();

            sb.AppendLine("ACTIVE THREAD (MOST RELEVANT PRIOR EXCHANGE)");
            sb.AppendLine(SeparatorLine);
            sb.AppendLine();

            // Render the remaining pinned tail (most recent first), excluding the last exchange used above.
            for (int i = pinnedTail.Count - 2; i >= 0; i--)
            {
                sb.AppendLine(FormatExchange(pinnedTail[i], sanitizeAssistant: false));
                sb.AppendLine();
            }

            // Reference-only context; downstream logic should not treat this section as authoritative.
            sb.AppendLine("CONTEXT FILLER (REFERENCE ONLY — DO NOT ADVANCE FROM HERE)");
            sb.AppendLine(SeparatorLine);
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
                    else
                    {
                        sb.AppendLine($"[Exchange {exchanges[i].Index} omitted: overflow too large ({block.Length} chars)]");
                    }
                }

                break;
            }

            return sb.ToString().Trim();
        }

        
        private static string FormatExchangeWhereWeLeftOff(Filter1BuildSeed.SeedExchange ex)
        {
            // WHERE WE LEFT OFF should anchor durable state, not transient verification checklists.
            // Keep assistant text rich, but remove obvious procedural/test-step blocks unless they carry an anchor tag.
            var sb = new StringBuilder();

            var msgLabel = $"Message {ex.Index} — USER";
            if (ex.UserLineIndex >= 0) msgLabel += $" (Source: Truth {FormatTruthRange(ex.UserLineIndex)})";
            msgLabel += ":";

            sb.AppendLine(msgLabel);
            sb.AppendLine(!string.IsNullOrWhiteSpace(ex.UserText) ? ex.UserText.Trim() : "[USER: empty]");
            sb.AppendLine();

            var respLabel = $"Response {ex.Index} — ASSISTANT";
            if (ex.AssistantLineIndex >= 0) respLabel += $" (Source: Truth {FormatTruthRange(ex.AssistantLineIndex)})";
            respLabel += ":";

            sb.AppendLine(respLabel);

            var assistant = !string.IsNullOrWhiteSpace(ex.AssistantText) ? ex.AssistantText.Trim() : "[ASSISTANT: empty]";
            sb.AppendLine(SanitizeAssistantForWwlo(assistant));

            return sb.ToString().TrimEnd();
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

            var msgLabel = $"Message {ex.Index} — USER";
            if (ex.UserLineIndex >= 0) msgLabel += $" (Source: Truth {FormatTruthRange(ex.UserLineIndex)})";
            msgLabel += ":";

            sb.AppendLine(msgLabel);
            sb.AppendLine(!string.IsNullOrWhiteSpace(ex.UserText) ? ex.UserText.Trim() : "[USER: empty]");

            sb.AppendLine();

            var respLabel = $"Response {ex.Index} — ASSISTANT";
            if (ex.AssistantLineIndex >= 0) respLabel += $" (Source: Truth {FormatTruthRange(ex.AssistantLineIndex)})";
            respLabel += ":";

            sb.AppendLine(respLabel);
            var assistant = !string.IsNullOrWhiteSpace(ex.AssistantText) ? ex.AssistantText.Trim() : "[ASSISTANT: empty]";
            sb.AppendLine(sanitizeAssistant ? SanitizeAssistantForWwlo(assistant) : assistant);

            return sb.ToString().TrimEnd();
        }

        private static string FormatTruthRange(int lineIndex) => $"{lineIndex}\u2013{lineIndex}";
    }
}
