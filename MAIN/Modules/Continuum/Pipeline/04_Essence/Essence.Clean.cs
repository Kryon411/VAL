using System;
using System.Text.RegularExpressions;
using VAL.Continuum.Pipeline.Filter2;

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
            s = s.Replace("\r\n", "\n").Replace("\r", "\n");

            // Trim trailing whitespace per line
            s = Regex.Replace(s, @"[ \t]+\n", "\n");

            // Preserve structural line breaks for labels/language markers.
            s = PreserveStructuralLineBreaks(s);

            // Normalize hyphen-only separator lines.
            s = Regex.Replace(s, @"(?m)^[\-]+$", Filter2Rules.Separator);

            // Remove common accidental UI markers (keep conservative)
            // Example: literal "[Screenshot hidden]" if it ever leaks into captured text
            s = Regex.Replace(s, @"\[Screenshot hidden\]", "", RegexOptions.IgnoreCase);

            return s;
        }

        private static string PreserveStructuralLineBreaks(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text ?? string.Empty;

            var labels = @"(?:USER|ASSISTANT)";
            text = Regex.Replace(text, $@"(?m)^(?<label>{labels}):[ \t]*\n[ \t]*(?=\S)", "${label}: ");
            text = Regex.Replace(text, $@"(?m)^(?<label>{labels}):[ \t]+(?=\S)", "${label}: ");
            text = Regex.Replace(text, $@"(?m)^(?<label>{labels}):(?=\S)", "${label}: ");

            var languages = @"(?:powershell|csharp|bash|zsh|sh|shell|cmd|console)";
            text = Regex.Replace(text, $@"(?im)^(?<lang>[ \t]*{languages})\s+(?=\S)", "${lang}\n");
            text = Regex.Replace(text, $@"(?im)^(?<lang>[ \t]*{languages})(?=[^\s])", "${lang}\n");

            return text;
        }
    }
}
