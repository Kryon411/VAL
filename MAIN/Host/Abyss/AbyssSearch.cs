using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using VAL.Continuum.Pipeline.Truth;

namespace VAL.Host.Abyss
{
    internal sealed class AbyssTruthLine
    {
        public int LineIndex { get; init; }
        public char Role { get; init; }
        public string Text { get; init; } = string.Empty;
    }

    internal sealed class AbyssExchange
    {
        public string ChatId { get; init; } = string.Empty;
        public string TruthPath { get; init; } = string.Empty;
        public DateTime LastWriteUtc { get; init; }

        public List<AbyssTruthLine> UserLines { get; } = new();
        public List<AbyssTruthLine> AssistantLines { get; } = new();

        public string UserText => string.Join("\n", UserLines.Select(l => l.Text));
        public string AssistantText => string.Join("\n", AssistantLines.Select(l => l.Text));
    }

    internal sealed class AbyssSearchResult
    {
        public AbyssExchange Exchange { get; init; } = new();
        public int Score { get; init; }
    }

    internal static class AbyssSearch
    {
        public static List<AbyssSearchResult> Search(string memoryRoot, string query, int maxResults, IReadOnlyCollection<string>? excludeFingerprints = null)
        {
            var results = new List<AbyssSearchResult>();
            var tokens = Tokenize(query);
            if (tokens.Count == 0)
                return results;

            if (string.IsNullOrWhiteSpace(memoryRoot) || !Directory.Exists(memoryRoot))
                return results;

            var exclusions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (excludeFingerprints != null)
            {
                foreach (var fingerprint in excludeFingerprints)
                {
                    if (!string.IsNullOrWhiteSpace(fingerprint))
                        exclusions.Add(fingerprint.Trim());
                }
            }

            foreach (var truthPath in EnumerateTruthLogs(memoryRoot))
            {
                foreach (var exchange in ReadExchanges(truthPath))
                {
                    var score = ScoreExchange(exchange, tokens);
                    if (score <= 0) continue;

                    if (exclusions.Count > 0)
                    {
                        var fingerprint = BuildFingerprint(exchange);
                        if (exclusions.Contains(fingerprint))
                            continue;
                    }

                    results.Add(new AbyssSearchResult
                    {
                        Exchange = exchange,
                        Score = score
                    });
                }
            }

            return results
                .OrderByDescending(r => r.Score)
                .ThenByDescending(r => r.Exchange.LastWriteUtc)
                .Take(Math.Max(1, maxResults))
                .ToList();
        }

        public static (int startLine, int endLine) GetLineRange(AbyssExchange exchange)
        {
            if (exchange == null)
                return (0, 0);

            var min = int.MaxValue;
            var max = -1;

            foreach (var line in exchange.UserLines)
            {
                min = Math.Min(min, line.LineIndex);
                max = Math.Max(max, line.LineIndex);
            }

            foreach (var line in exchange.AssistantLines)
            {
                min = Math.Min(min, line.LineIndex);
                max = Math.Max(max, line.LineIndex);
            }

            if (max < 0 || min == int.MaxValue)
                return (0, 0);

            return (min, max);
        }

        public static string BuildFingerprint(AbyssExchange exchange)
        {
            if (exchange == null)
                return string.Empty;

            var (startLine, endLine) = GetLineRange(exchange);
            return $"{exchange.ChatId}:{startLine}-{endLine}";
        }

        public static List<AbyssExchange> GetLastFromMostRecent(string memoryRoot, int count)
        {
            var list = new List<AbyssExchange>();
            if (string.IsNullOrWhiteSpace(memoryRoot) || !Directory.Exists(memoryRoot))
                return list;

            var latest = GetMostRecentTruthLog(memoryRoot);
            if (string.IsNullOrWhiteSpace(latest))
                return list;

            var exchanges = ReadExchanges(latest);
            if (exchanges.Count == 0)
                return list;

            count = Math.Max(1, Math.Min(3, count));
            for (int i = Math.Max(0, exchanges.Count - count); i < exchanges.Count; i++)
            {
                list.Add(exchanges[i]);
            }

            list.Reverse();
            return list;
        }

        private static string UnescapeTruthPayload(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            // Truth.log may contain literal escape sequences (\n, \r, \t, \" etc).
            // Convert the common ones so snippets and injected text render correctly.
            return s
                .Replace("\\r\\n", "\n")
                .Replace("\\n", "\n")
                .Replace("\\r", "\n")
                .Replace("\\t", "\t")
                .Replace("\\\"", "\"");
        }

        private static IEnumerable<string> EnumerateTruthLogs(string memoryRoot)
        {
            try
            {
                return Directory.EnumerateFiles(memoryRoot, "Truth.log", SearchOption.AllDirectories);
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private static string? GetMostRecentTruthLog(string memoryRoot)
        {
            string? latestPath = null;
            DateTime latestUtc = DateTime.MinValue;

            foreach (var path in EnumerateTruthLogs(memoryRoot))
            {
                try
                {
                    var utc = File.GetLastWriteTimeUtc(path);
                    if (utc > latestUtc)
                    {
                        latestUtc = utc;
                        latestPath = path;
                    }
                }
                catch { }
            }

            return latestPath;
        }

        private static List<AbyssExchange> ReadExchanges(string truthPath)
        {
            var exchanges = new List<AbyssExchange>();
            if (string.IsNullOrWhiteSpace(truthPath) || !File.Exists(truthPath))
                return exchanges;

            var chatId = new DirectoryInfo(Path.GetDirectoryName(truthPath) ?? string.Empty).Name;
            var lastWriteUtc = DateTime.MinValue;
            try { lastWriteUtc = File.GetLastWriteTimeUtc(truthPath); } catch { }

            AbyssExchange? current = null;

            foreach (var entry in TruthReader.Read(truthPath, repairTailFirst: true))
            {
                var payload = UnescapeTruthPayload(entry.Payload);
                var line = new AbyssTruthLine
                {
                    LineIndex = entry.LineNumber,
                    Role = entry.Role,
                    Text = payload
                };

                if (entry.Role == 'U')
                {
                    if (current != null && (current.UserLines.Count > 0 || current.AssistantLines.Count > 0))
                        exchanges.Add(current);

                    current = new AbyssExchange
                    {
                        ChatId = chatId,
                        TruthPath = truthPath,
                        LastWriteUtc = lastWriteUtc
                    };
                    current.UserLines.Add(line);
                }
                else
                {
                    if (current == null)
                    {
                        current = new AbyssExchange
                        {
                            ChatId = chatId,
                            TruthPath = truthPath,
                            LastWriteUtc = lastWriteUtc
                        };
                    }
                    current.AssistantLines.Add(line);
                }
            }

            if (current != null && (current.UserLines.Count > 0 || current.AssistantLines.Count > 0))
                exchanges.Add(current);

            return exchanges;
        }

        private static int ScoreExchange(AbyssExchange exchange, IReadOnlyList<string> tokens)
        {
            if (tokens.Count == 0)
                return 0;

            var userText = exchange.UserText;
            var assistantText = exchange.AssistantText;
            var combined = string.Concat(userText, "\n", assistantText);

            var score = 0;
            foreach (var token in tokens)
            {
                if (string.IsNullOrWhiteSpace(token))
                    continue;

                var hits = CountOccurrences(combined, token);
                if (hits <= 0) continue;

                score += hits;
                score += CountOccurrences(userText, token);
            }

            return score;
        }

        private static int CountOccurrences(string text, string token)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(token))
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

        private static List<string> Tokenize(string query)
        {
            var tokens = new List<string>();
            if (string.IsNullOrWhiteSpace(query))
                return tokens;

            var sb = new StringBuilder();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var ch in query)
            {
                if (char.IsLetterOrDigit(ch))
                {
                    sb.Append(ch);
                }
                else
                {
                    FlushToken(sb, tokens, seen);
                }
            }

            FlushToken(sb, tokens, seen);
            return tokens;
        }

        private static void FlushToken(StringBuilder sb, List<string> tokens, HashSet<string> seen)
        {
            if (sb.Length == 0)
                return;

            var token = sb.ToString();
            sb.Clear();

            if (token.Length < 2)
                return;

            if (seen.Add(token))
                tokens.Add(token);
        }
    }
}
