using System;
using System.Text;
using System.Text.RegularExpressions;

namespace VAL.Continuum.Pipeline.Essence
{
    public static class EssenceClean
    {
        // Mechanical cleanup ONLY:
        // - normalize newlines
        // - collapse excessive blank lines
        // - remove obvious UI artifacts if they leak into text (rare)
        // - do NOT summarize, drop turns, or apply budgets
        public static string CleanWorkingSet(string workingSet)
        {
            if (string.IsNullOrWhiteSpace(workingSet))
                return string.Empty;

            var s = workingSet;

            // Normalize newlines
            s = s.Replace("\r\n", "\n");

            // Trim trailing whitespace per line
            s = Regex.Replace(s, @"[ \t]+\n", "\n");

            // Collapse >2 blank lines into 2
            s = Regex.Replace(s, @"\n{3,}", "\n\n");

            // Remove common accidental UI markers (keep conservative)
            // Example: literal "[Screenshot hidden]" if it ever leaks into captured text
            s = Regex.Replace(s, @"\[Screenshot hidden\]", "", RegexOptions.IgnoreCase);

            // Final trim
            return s.Trim();
        }
    }
}
