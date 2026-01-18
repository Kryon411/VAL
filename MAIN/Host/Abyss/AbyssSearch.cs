using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VAL.Continuum.Pipeline.Truth;

namespace VAL.Host.Abyss
{
    internal static class AbyssSearch
    {
        internal sealed class TruthLine
        {
            public char Role { get; init; }
            public int LineNumber { get; init; }
            public string Text { get; init; } = string.Empty;
        }

        internal sealed class Exchange
        {
            public int UserLineNumber { get; set; }
            public string UserText { get; set; } = string.Empty;
            public int AssistantLineStart { get; set; }
            public int AssistantLineEnd { get; set; }
            public List<string> AssistantLines { get; } = new List<string>();
        }

        internal sealed class AbyssSearchResult
        {
            public string SessionId { get; init; } = string.Empty;
            public string SessionFolder { get; init; } = string.Empty;
            public string TruthPath { get; init; } = string.Empty;
            public int UserLineNumber { get; init; }
            public int AssistantLineStart { get; init; }
            public int AssistantLineEnd { get; init; }
            public string UserText { get; init; } = string.Empty;
            public List<string> AssistantLines { get; init; } = new List<string>();
            public int Score { get; init; }
            public DateTime LastWriteUtc { get; init; }
        }

        public static IReadOnlyList<AbyssSearchResult> Search(string chatsRoot, string query, int limit)
        {
            var results = new List<AbyssSearchResult>();
            if (string.IsNullOrWhiteSpace(chatsRoot) || !Directory.Exists(chatsRoot))
                return results;

            var tokens = Tokenize(query);
            if (tokens.Length == 0)
                return results;

            IEnumerable<string> truthLogs;
            try
            {
                truthLogs = Directory.EnumerateFiles(chatsRoot, TruthStorage.TruthFileName, SearchOption.AllDirectories);
            }
            catch
            {
                return results;
            }

            foreach (var truthPath in truthLogs)
            {
                List<TruthLine> lines;
                try
                {
                    lines = ParseTruthLog(truthPath);
                }
                catch
                {
                    continue;
                }

                if (lines.Count == 0)
                    continue;

                var exchanges = BuildExchanges(lines);
                if (exchanges.Count == 0)
                    continue;

                var sessionFolder = new DirectoryInfo(Path.GetDirectoryName(truthPath) ?? string.Empty).Name;
                var lastWriteUtc = SafeGetLastWriteUtc(truthPath);

                foreach (var exchange in exchanges)
                {
                    var score = ScoreExchange(exchange, tokens);
                    if (score <= 0)
                        continue;

                    results.Add(new AbyssSearchResult
                    {
                        SessionId = sessionFolder,
                        SessionFolder = sessionFolder,
                        TruthPath = truthPath,
                        UserLineNumber = exchange.UserLineNumber,
                        AssistantLineStart = exchange.AssistantLineStart,
                        AssistantLineEnd = exchange.AssistantLineEnd,
                        UserText = exchange.UserText,
                        AssistantLines = new List<string>(exchange.AssistantLines),
                        Score = score,
                        LastWriteUtc = lastWriteUtc
                    });
                }
            }

            return results
                .OrderByDescending(r => r.Score)
                .ThenByDescending(r => r.LastWriteUtc)
                .Take(Math.Max(1, limit))
                .ToList();
        }

        public static IReadOnlyList<AbyssSearchResult> TakeLastExchanges(string truthPath, int maxExchanges)
        {
            var results = new List<AbyssSearchResult>();
            if (string.IsNullOrWhiteSpace(truthPath) || !File.Exists(truthPath))
                return results;

            var lines = ParseTruthLog(truthPath);
            if (lines.Count == 0)
                return results;

            var exchanges = BuildExchanges(lines);
            if (exchanges.Count == 0)
                return results;

            var sessionFolder = new DirectoryInfo(Path.GetDirectoryName(truthPath) ?? string.Empty).Name;
            var lastWriteUtc = SafeGetLastWriteUtc(truthPath);

            foreach (var exchange in exchanges.TakeLast(Math.Max(1, maxExchanges)))
            {
                results.Add(new AbyssSearchResult
                {
                    SessionId = sessionFolder,
                    SessionFolder = sessionFolder,
                    TruthPath = truthPath,
                    UserLineNumber = exchange.UserLineNumber,
                    AssistantLineStart = exchange.AssistantLineStart,
                    AssistantLineEnd = exchange.AssistantLineEnd,
                    UserText = exchange.UserText,
                    AssistantLines = new List<string>(exchange.AssistantLines),
                    Score = 1,
                    LastWriteUtc = lastWriteUtc
                });
            }

            return results;
        }

        private static List<TruthLine> ParseTruthLog(string truthPath)
        {
            var lines = new List<TruthLine>();
            if (string.IsNullOrWhiteSpace(truthPath) || !File.Exists(truthPath))
                return lines;

            var rawLines = File.ReadAllLines(truthPath);
            for (var i = 0; i < rawLines.Length; i++)
            {
                var line = rawLines[i] ?? string.Empty;
                if (line.Length < 2)
                    continue;

                var role = line[0];
                if (line[1] != '|')
                    continue;

                if (role != 'U' && role != 'A')
                    continue;

                lines.Add(new TruthLine
                {
                    Role = role,
                    LineNumber = i + 1,
                    Text = line.Substring(2)
                });
            }

            return lines;
        }

        private static List<Exchange> BuildExchanges(IReadOnlyList<TruthLine> lines)
        {
            var exchanges = new List<Exchange>();
            Exchange? current = null;

            foreach (var line in lines)
            {
                if (line.Role == 'U')
                {
                    if (current != null)
                        exchanges.Add(current);

                    current = new Exchange
                    {
                        UserLineNumber = line.LineNumber,
                        UserText = line.Text
                    };
                    continue;
                }

                if (line.Role == 'A')
                {
                    current ??= new Exchange();
                    if (current.AssistantLineStart == 0)
                        current.AssistantLineStart = line.LineNumber;

                    current.AssistantLineEnd = line.LineNumber;
                    current.AssistantLines.Add(line.Text);
                }
            }

            if (current != null)
                exchanges.Add(current);

            return exchanges;
        }

        private static int ScoreExchange(Exchange exchange, IReadOnlyList<string> tokens)
        {
            if (tokens.Count == 0)
                return 0;

            var combined = string.Concat(
                exchange.UserText ?? string.Empty,
                "\n",
                string.Join("\n", exchange.AssistantLines));

            var score = 0;
            foreach (var token in tokens)
            {
                var tokenScore = CountOccurrences(combined, token);
                if (tokenScore == 0)
                    continue;

                score += tokenScore;
                if (!string.IsNullOrWhiteSpace(exchange.UserText))
                    score += CountOccurrences(exchange.UserText, token);
            }

            return score;
        }

        private static string[] Tokenize(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return Array.Empty<string>();

            return query
                .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => t.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static int CountOccurrences(string text, string token)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(token))
                return 0;

            var count = 0;
            var index = 0;
            while (true)
            {
                index = text.IndexOf(token, index, StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                    break;

                count++;
                index += token.Length;
            }

            return count;
        }

        private static DateTime SafeGetLastWriteUtc(string path)
        {
            try { return File.GetLastWriteTimeUtc(path); } catch { return DateTime.MinValue; }
        }
    }
}
