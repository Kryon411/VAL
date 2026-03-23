using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using VAL.Continuum.Pipeline;

namespace VAL.Continuum.Pipeline.Truth
{
    public sealed class TruthViewBuilder : ITruthViewBuilder
    {
        private readonly ITruthStore _truthStore;

        public TruthViewBuilder(ITruthStore truthStore)
        {
            _truthStore = truthStore ?? throw new ArgumentNullException(nameof(truthStore));
        }

        public TruthView BuildView(string chatId)
        {
            if (string.IsNullOrWhiteSpace(chatId))
                throw new ArgumentNullException(nameof(chatId));

            var truthPath = _truthStore.GetTruthPath(chatId);
            var messages = new List<TruthMessage>();

            if (!File.Exists(truthPath))
            {
                return new TruthView
                {
                    ChatId = chatId,
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
                    ChatId = chatId,
                    Messages = messages
                };
            }

            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                var rawLine = lines[lineIndex];
                if (string.IsNullOrWhiteSpace(rawLine))
                    continue;

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
                        role = TruthRole.User;
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

            TryWriteViewFile(truthPath, view);
            return view;
        }

        private static string DecodeEscapedNewlines(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;

            return s.Replace("\\r\\n", "\n").Replace("\\n", "\n").Replace("\\r", "\n");
        }

        private static string StripUiChrome(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;

            s = s.Replace("text\nCopy code\n", string.Empty);
            s = s.Replace("Copy code\n", string.Empty);
            s = s.Replace("\nCopy code", string.Empty);
            s = s.Replace("\nDocument\n", "\n");
            s = s.Replace("ChatGPT said:", string.Empty);
            s = s.Replace("ChatGPT said", string.Empty);

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

                foreach (var message in view.Messages)
                {
                    var header = message.Role == TruthRole.Assistant ? "ASSISTANT:" : "USER:";
                    sb.Append(header).Append(' ').AppendLine((message.Text ?? string.Empty).Trim());
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
