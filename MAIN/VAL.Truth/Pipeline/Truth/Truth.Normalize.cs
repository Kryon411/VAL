using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using VAL.Continuum.Pipeline;
using VAL.Continuum.Pipeline.Truth;

namespace VAL.Continuum.Pipeline.Truth
{
    /// <summary>
    /// Structural-only Truth normalization.
    /// - Does NOT filter / summarize Truth.log.
    /// - Decodes escaped newlines (\\n) back into real newlines so paragraph logic works downstream.
    /// - Removes obvious UI chrome lines that pollute tag detection (Copy code, Document, etc.).
    /// </summary>
    public static class TruthNormalize
    {
        public static TruthView BuildView(string chatId)
        {
            if (string.IsNullOrWhiteSpace(chatId))
                throw new ArgumentNullException(nameof(chatId));

            var truthPath = TruthStorage.GetTruthPath(chatId);
            var messages = new List<TruthMessage>();

            if (!File.Exists(truthPath))
            {
                return new TruthView
                {
                    Messages = messages
                };
            }

            string[] lines;
            try
            {
                lines = File.ReadAllLines(truthPath);
            }
            catch
            {
                return new TruthView
                {
                    Messages = messages
                };
            }

            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                var rawLine = lines[lineIndex];
                if (string.IsNullOrWhiteSpace(rawLine))
                    continue;

                // Format: "U|..." or "A|..."
                var role = TruthRole.User;
                var text = rawLine;

                if (rawLine.Length >= 2 && rawLine[1] == '|')
                {
                    var prefix = rawLine[0];
                    text = rawLine.Substring(2);

                    if (prefix == 'A')
                        role = TruthRole.Assistant;
                    else if (prefix == 'U')
                        role = TruthRole.User;
                    else
                    {
                        // Unknown prefix: treat as user intent authority to avoid accidental loss.
                        role = TruthRole.User;
                    }
                }

                text = DecodeEscapedNewlines(text);
                text = StripUiChrome(text);
                text = text.Trim();

                if (string.IsNullOrWhiteSpace(text))
                    continue;

                messages.Add(new TruthMessage
                {
                    Role = role,
                    Text = text,
                    LineIndex = lineIndex
                });
            }

            var view = new TruthView
            {
                ChatId = chatId,
                Messages = messages
            };

            // Optional audit renderer (human-readable)
            TryWriteViewFile(truthPath, view);

            return view;
        }

        private static string DecodeEscapedNewlines(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;

            // Truth.log encodes newlines as literal "\n" for line-based append-only storage.
            // Convert them back so paragraph splitting works.
            return s.Replace("\\r\\n", "\n").Replace("\\n", "\n").Replace("\\r", "\n");
        }

        private static string StripUiChrome(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;

            // Remove common capture artifacts that pollute downstream selection.
            // Keep this conservative; Truth.log remains the source-of-truth.
            s = s.Replace("text\nCopy code\n", string.Empty);
            s = s.Replace("Copy code\n", string.Empty);
            s = s.Replace("\nCopy code", string.Empty);

            // Attachment noise
            s = s.Replace("\nDocument\n", "\n");

            // "ChatGPT said:" prefix (sometimes glued: "ChatGPT said:text...")
            s = s.Replace("ChatGPT said:", string.Empty);
            s = s.Replace("ChatGPT said", string.Empty);

            // Fix common glue artifact: "textVAL ..." at start
            if (s.StartsWith("textVAL ", StringComparison.OrdinalIgnoreCase))
                s = s.Substring(4);

            return s;
        }

        private static void TryWriteViewFile(string truthPath, TruthView view)
        {
            try
            {
                var dir = Path.GetDirectoryName(truthPath) ?? AppContext.BaseDirectory;
                var viewPath = Path.Combine(dir, "Truth.view");
                var sb = new StringBuilder();

                foreach (var m in view.Messages)
                {
                    var header = m.Role == TruthRole.Assistant ? "ASSISTANT:" : "USER:";
                    sb.Append(header).Append(' ').AppendLine((m.Text ?? string.Empty).Trim());
                    sb.AppendLine();
                }

                AtomicFile.WriteAllTextAtomic(viewPath, sb.ToString().Trim());
            }
            catch
            {
                // Non-fatal
            }
        }
    }
}
