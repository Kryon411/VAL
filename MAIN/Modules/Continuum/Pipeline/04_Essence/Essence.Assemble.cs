using System;
using System.Text;
using System.Text.RegularExpressions;
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

            // The Essence payload is a plain-text snapshot. Context.txt is injected separately.
            return s.Trim();
        }
    }
}
