using System;
using System.IO;

namespace VAL.Continuum.Pipeline
{
    /// <summary>
    /// Loads Continuum context text files from disk.
    ///
    /// - Context.txt           : Pulse context (used by Essence/Pulse)
    /// - Context.Prelude.txt   : Manual Prelude context (used by Prelude button)
    ///
    /// Notes:
    /// - This class is intentionally "boring": best-effort IO, never throws.
    /// - Kept in VAL.Continuum.Pipeline namespace for compatibility with existing call sites.
    /// </summary>
    public static class ContinuumPreamble
    {
        // ----------------------
        // Pulse (Context.txt)
        // ----------------------

        public static string LoadPreamble() => LoadPreamble(string.Empty);

        // chatId is accepted for historical compatibility (some callers pass it in).
        // Context.txt itself does not contain per-chat information.
        public static string LoadPreamble(string chatId)
        {
            try
            {
                var path = FindContextPath();
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    return string.Empty;

                return File.ReadAllText(path);
            }
            catch
            {
                return string.Empty;
            }
        }

        // Compatibility aliases (older call sites).
        public static string Load() => LoadPreamble();
        public static string Load(string chatId) => LoadPreamble(chatId);

        // ----------------------
        // Prelude (Context.Prelude.txt)
        // ----------------------

        public static string LoadPrelude() => LoadPrelude(string.Empty);

        // chatId is accepted for historical compatibility (some callers pass it in).
        // Context.Prelude.txt itself does not contain per-chat information.
        public static string LoadPrelude(string chatId)
        {
            try
            {
                var path = FindPreludePath();
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    return string.Empty;

                return File.ReadAllText(path);
            }
            catch
            {
                return string.Empty;
            }
        }

        // ----------------------
        // Essence injection helpers
        // ----------------------

        /// <summary>
        /// Prepends Context.txt to the provided essence text (if present), separated by blank lines.
        /// </summary>
        public static string InjectIntoEssence(string essenceText) => PrependContextToEssence(essenceText, string.Empty);

        /// <summary>
        /// Compatibility helper used by older pipeline stages.
        ///
        /// IMPORTANT:
        /// Some historical call sites accidentally pass (essenceText, chatId) because both parameters are strings,
        /// and C# can't catch the swap at compile-time.
        ///
        /// This method defensively accepts either order and produces a stable result:
        /// Context.txt + "\n\n" + essenceText.
        /// </summary>
        public static string InjectIntoEssence(string essenceText, string chatId)
        {
            try
            {
                var e = essenceText ?? string.Empty;
                var id = chatId ?? string.Empty;

                // Defensive swap: some historical call sites may still call InjectIntoEssence(chatId, essenceText)
                // since both parameters are strings. We treat the *essence-like* value as the essence payload.
                if (LooksLikeChatId(e) && LooksLikeEssenceText(id) && !LooksLikeChatId(id))
                {
                    var tmp = e;
                    e = id;
                    id = tmp;
                }

                return PrependContextToEssence(e, id);
            }
            catch
            {
                return PrependContextToEssence(essenceText, chatId);
            }
        }


        /// <summary>
        /// Prepends Context.txt to essence text (if present), separated by blank lines.
        /// </summary>
        public static string PrependContextToEssence(string essenceText) => PrependContextToEssence(essenceText, string.Empty);

        /// <summary>
        /// Prepends Context.txt to essence text (if present), separated by blank lines.
        /// Also defends against chatId/essenceText being passed in the wrong order by legacy call sites.
        /// </summary>
        public static string PrependContextToEssence(string essenceText, string chatId)
        {
            try
            {
                var e = essenceText ?? string.Empty;
                var id = chatId ?? string.Empty;

                // Defensive swap: some call sites may call PrependContextToEssence(chatId, essenceText).
                if (LooksLikeChatId(e) && LooksLikeEssenceText(id) && !LooksLikeChatId(id))
                {
                    var tmp = e;
                    e = id;
                    id = tmp;
                }

                var preamble = LoadPreamble(id);
                if (string.IsNullOrWhiteSpace(preamble))
                    return e;

                var trimmedPreamble = preamble.Trim();
                var trimmedEssence = e.Trim();

                // Idempotency: if the essence already begins with the current preamble, don't prepend again.
                // (This avoids accidental double-injection when Pulse/Prelude flows overlap.)
                if (IsAlreadyPrepended(trimmedEssence, trimmedPreamble))
                    return e;

                if (string.IsNullOrWhiteSpace(trimmedEssence))
                    return preamble;

                var preambleWithGap = EnsureEndsWithBlankLine(preamble);
                return preambleWithGap + e;
            }
            catch
            {
                return essenceText ?? string.Empty;
            }
        }

        private static bool IsAlreadyPrepended(string trimmedEssence, string trimmedPreamble)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(trimmedEssence) || string.IsNullOrWhiteSpace(trimmedPreamble))
                    return false;

                if (trimmedEssence.StartsWith(trimmedPreamble, StringComparison.Ordinal))
                    return true;

                // Soft-match by first non-empty line (protects against minor whitespace differences).
                var pLine = FirstNonEmptyLine(trimmedPreamble);
                var eLine = FirstNonEmptyLine(trimmedEssence);

                if (!string.IsNullOrWhiteSpace(pLine) &&
                    !string.IsNullOrWhiteSpace(eLine) &&
                    string.Equals(pLine.Trim(), eLine.Trim(), StringComparison.Ordinal))
                    return true;

                return false;
            }
            catch
            {
                return false;
            }
        }

        private static string FirstNonEmptyLine(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;

            using (var sr = new StringReader(s))
            {
                string? line;
                while ((line = sr.ReadLine()) != null)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                        return line;
                }
            }
            return string.Empty;
        }

        private static bool LooksLikeEssenceText(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;

            // Essence / seeded text is almost always multi-line.
            if (s.IndexOf('\n') >= 0) return true;

            // If it's long and contains whitespace, it's almost certainly not a chat/session id.
            if (s.Length >= 120 && ContainsAnyWhitespace(s)) return true;

            // Common transcript markers (IndexOf used for broad framework compatibility).
            if (s.IndexOf("ASSISTANT:", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (s.IndexOf("USER:", StringComparison.OrdinalIgnoreCase) >= 0) return true;

            return false;
        }

        private static bool LooksLikeChatId(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            if (ContainsAnyWhitespace(s)) return false;

            // Typical: GUID-ish or "session-xxxx".
            if (s.StartsWith("session-", StringComparison.OrdinalIgnoreCase))
                return true;

            // GUID-ish heuristic (not strict regex, but safe enough for disambiguation).
            if (s.Length >= 16 && s.Length <= 64 && s.IndexOf('-') >= 0)
                return true;

            return false;
        }

        private static bool ContainsAnyWhitespace(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                if (char.IsWhiteSpace(s[i]))
                    return true;
            }
            return false;
        }

        private static string EnsureEndsWithBlankLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            if (EndsWithBlankLine(text))
                return text;

            if (EndsWithLineBreak(text))
                return text + Environment.NewLine;

            return text + Environment.NewLine + Environment.NewLine;
        }

        private static bool EndsWithBlankLine(string text)
        {
            if (text.Length >= 2 &&
                text[text.Length - 1] == '\n' &&
                text[text.Length - 2] == '\n')
                return true;

            if (text.Length >= 4 &&
                text[text.Length - 1] == '\n' &&
                text[text.Length - 2] == '\r' &&
                text[text.Length - 3] == '\n' &&
                text[text.Length - 4] == '\r')
                return true;

            return false;
        }

        private static bool EndsWithLineBreak(string text)
        {
            if (text.Length >= 1 && text[text.Length - 1] == '\n')
                return true;

            if (text.Length >= 2 &&
                text[text.Length - 1] == '\n' &&
                text[text.Length - 2] == '\r')
                return true;

            return false;
        }

        // ----------------------
        // Path resolution
        // ----------------------

        private static string? FindContextPath()
        {
            try
            {
                var starts = new[]
                {
                    AppContext.BaseDirectory,
                    Directory.GetCurrentDirectory()
                };

                for (int i = 0; i < starts.Length; i++)
                {
                    var found = FindContextPathFrom(starts[i]);
                    if (!string.IsNullOrWhiteSpace(found))
                        return found;
                }
            }
            catch { }

            return null;
        }

        private static string? FindPreludePath()
        {
            try
            {
                var starts = new[]
                {
                    AppContext.BaseDirectory,
                    Directory.GetCurrentDirectory()
                };

                for (int i = 0; i < starts.Length; i++)
                {
                    var found = FindPreludePathFrom(starts[i]);
                    if (!string.IsNullOrWhiteSpace(found))
                        return found;
                }
            }
            catch { }

            return null;
        }

        private static string? FindContextPathFrom(string startDir)
        {
            try
            {
                var dir = startDir;
                for (int i = 0; i < 10; i++)
                {
                    if (string.IsNullOrWhiteSpace(dir)) break;

                    // <root>\Modules\Continuum\Context.txt
                    var p1 = Path.Combine(dir, "Modules", "Continuum", "Context.txt");
                    if (File.Exists(p1)) return p1;

                    // <root>\MAIN\Modules\Continuum\Context.txt
                    var p2 = Path.Combine(dir, "MAIN", "Modules", "Continuum", "Context.txt");
                    if (File.Exists(p2)) return p2;

                    var parent = Directory.GetParent(dir);
                    dir = parent != null ? parent.FullName : string.Empty;
                }
            }
            catch { }

            return null;
        }

        private static string? FindPreludePathFrom(string startDir)
        {
            try
            {
                var dir = startDir;
                for (int i = 0; i < 10; i++)
                {
                    if (string.IsNullOrWhiteSpace(dir)) break;

                    // <root>\Modules\Continuum\Context.Prelude.txt
                    var p1 = Path.Combine(dir, "Modules", "Continuum", "Context.Prelude.txt");
                    if (File.Exists(p1)) return p1;

                    // <root>\MAIN\Modules\Continuum\Context.Prelude.txt
                    var p2 = Path.Combine(dir, "MAIN", "Modules", "Continuum", "Context.Prelude.txt");
                    if (File.Exists(p2)) return p2;

                    var parent = Directory.GetParent(dir);
                    dir = parent != null ? parent.FullName : string.Empty;
                }
            }
            catch { }

            return null;
        }
    }
}
