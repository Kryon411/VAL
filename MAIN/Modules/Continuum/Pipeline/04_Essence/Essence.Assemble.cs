using System;
using System.Text;
using System.Text.RegularExpressions;
using VAL.Continuum.Pipeline.Common;

namespace VAL.Continuum.Pipeline.Essence
{
    public static class EssenceAssemble
    {
        // Assemble a condensed transcript from WorkingSet text.
        // This is NOT a semantic summarizer; it's formatting + light compaction.
        // Preserve narrative continuity and constraints as they appear.
        public static string AssembleEssenceM(string chatId, string cleanedWorkingSet)
        {
            if (string.IsNullOrWhiteSpace(cleanedWorkingSet))
                return string.Empty;

            // Deterministic assembly:
            // - keep role headers (USER/ASSISTANT)
            // - ensure double-newline paragraph boundaries
            // - compact obvious repeated role headers (rare)
            // - keep within ~25k chars naturally (Gate already enforced)

            var s = cleanedWorkingSet.Replace("\r\n", "\n").Trim();

            // Ensure role headers start on new paragraphs.
            s = Regex.Replace(s, @"\n(?=(USER:|ASSISTANT:|USER \(intent\):|ASSISTANT \(tag\):|TURN:))", "\n\n");

            // Collapse accidental triple blank lines
            s = Regex.Replace(s, @"\n{3,}", "\n\n");

            // Add a small, stable header for the new chat (no meta, no instructions).
            // Keep it short; the transcript itself is the product.
            var sb = new StringBuilder(s.Length + 256);

            // Continuum guardrail preamble (Context.txt)
            var preamble = ContinuumPreamble.Load();
            if (!string.IsNullOrWhiteSpace(preamble))
            {
                sb.AppendLine(preamble.Trim());
                sb.AppendLine();
            }


            sb.AppendLine("VAL CONTINUUM — ESSENCE-M (PULSE)");
            sb.AppendLine($"chatId: {chatId}");
            sb.AppendLine();
            sb.AppendLine(s.Trim());

            return sb.ToString().Trim();
        }
    }
}
