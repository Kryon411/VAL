using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using VAL.Continuum.Pipeline.Truth;

namespace VAL.Host.Abyss
{
    internal static class AbyssRuntime
    {
        private static readonly object Gate = new();
        private static Action<string>? _postJson;
        private static List<AbyssSearchResult> _lastResults = new();
        private static string? _lastQuery;
        private static string? _lastQueryOriginal;
        private static string? _lastMemoryRoot;
        private static string? _lastGeneratedUtc;

        public static void Initialize(Action<string> postJson)
        {
            _postJson = postJson;
        }

        public static void InjectPrompt(string? chatId)
        {
            var prompt = "Abyss query: <keywords>";
            SendInjectText(prompt, chatId);
        }

        public static void Search(string? chatId, string query, int maxResults, string? queryOriginal = null)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                ToastHub.TryShow(ToastKey.AbyssNoQuery, chatId: chatId);
                return;
            }

            ToastHub.TryShow(ToastKey.AbyssSearching, chatId: chatId);

            Task.Run(() =>
            {
                var memoryRoot = ResolveMemoryRoot();
                TrackQuery(query, queryOriginal, memoryRoot);
                if (string.IsNullOrWhiteSpace(memoryRoot) || !Directory.Exists(memoryRoot))
                {
                    ClearLastResults();
                    ToastHub.TryShow(ToastKey.AbyssNoTruthLogs, chatId: chatId);
                    EmitResults(chatId);
                    return;
                }

                var results = AbyssSearch.Search(memoryRoot, query, maxResults);
                lock (Gate)
                {
                    _lastResults = results;
                }

                if (results.Count == 0)
                {
                    ToastHub.TryShow(ToastKey.AbyssNoMatches, chatId: chatId);
                }
                else
                {
                    ToastHub.TryShow(
                        ToastKey.AbyssMatches,
                        chatId: chatId,
                        titleOverride: $"Abyss: {results.Count} matches");
                }

                var resultsPath = WriteResultsFile(chatId, query, results);
                if (!string.IsNullOrWhiteSpace(resultsPath))
                    ToastHub.TryShow(ToastKey.AbyssResultsWritten, chatId: chatId);

                EmitResults(chatId);
            });
        }

        public static void FetchLast(string? chatId, int count, bool inject)
        {
            ToastHub.TryShow(ToastKey.AbyssSearching, chatId: chatId);

            Task.Run(() =>
            {
                var memoryRoot = ResolveMemoryRoot();
                TrackQuery("(Last)", null, memoryRoot);
                if (string.IsNullOrWhiteSpace(memoryRoot) || !Directory.Exists(memoryRoot))
                {
                    ClearLastResults();
                    ToastHub.TryShow(ToastKey.AbyssNoTruthLogs, chatId: chatId);
                    EmitResults(chatId);
                    return;
                }

                var exchanges = AbyssSearch.GetLastFromMostRecent(memoryRoot, count);
                var results = exchanges.Select(ex => new AbyssSearchResult { Exchange = ex, Score = 0 }).ToList();

                lock (Gate)
                {
                    _lastResults = results;
                }

                if (results.Count == 0)
                {
                    ClearLastResults();
                    ToastHub.TryShow(ToastKey.AbyssNoMatches, chatId: chatId);
                    EmitResults(chatId);
                    return;
                }

                ToastHub.TryShow(
                    ToastKey.AbyssMatches,
                    chatId: chatId,
                    titleOverride: $"Abyss: {results.Count} matches");

                var resultsPath = WriteResultsFile(chatId, "(Last)", results);
                if (!string.IsNullOrWhiteSpace(resultsPath))
                    ToastHub.TryShow(ToastKey.AbyssResultsWritten, chatId: chatId);

                EmitResults(chatId);

                if (inject)
                {
                    InjectResults(new[] { 1 }, chatId);
                }
            });
        }

        public static void InjectResults(IReadOnlyList<int> indices, string? chatId = null)
        {
            if (indices == null || indices.Count == 0)
            {
                ToastHub.TryShow(ToastKey.AbyssNoSelection, chatId: chatId);
                return;
            }

            List<AbyssSearchResult> snapshot;
            lock (Gate)
            {
                snapshot = _lastResults.ToList();
            }

            if (snapshot.Count == 0)
            {
                ToastHub.TryShow(ToastKey.AbyssNoMatches, chatId: chatId);
                return;
            }

            var selected = new List<AbyssSearchResult>();
            foreach (var idx in indices)
            {
                if (idx <= 0 || idx > snapshot.Count)
                    continue;

                selected.Add(snapshot[idx - 1]);
                if (selected.Count >= 3)
                    break;
            }

            if (selected.Count == 0)
            {
                ToastHub.TryShow(ToastKey.AbyssNoSelection, chatId: chatId);
                return;
            }

            var payload = BuildInjectPayload(selected);
            SendInjectText(payload, chatId);

            ToastHub.TryShow(
                ToastKey.AbyssInjected,
                chatId: chatId,
                titleOverride: $"Abyss: injected result #{indices[0]}");
        }

        public static void EmitResults(string? chatId)
        {
            if (_postJson == null)
                return;

            var payload = BuildResultsPayload();
            if (payload == null)
                return;

            SendJson(payload);
        }

        public static void OpenSource(string? truthPath, string? chatId)
        {
            if (string.IsNullOrWhiteSpace(truthPath))
            {
                ToastHub.TryShow(ToastKey.ActionUnavailable, chatId: chatId, bypassLaunchQuiet: true);
                return;
            }

            try
            {
                if (!File.Exists(truthPath))
                {
                    ToastHub.TryShow(ToastKey.ActionUnavailable, chatId: chatId, bypassLaunchQuiet: true);
                    return;
                }

                Process.Start(new ProcessStartInfo { FileName = truthPath, UseShellExecute = true });
            }
            catch
            {
                ToastHub.TryShow(ToastKey.ActionUnavailable, chatId: chatId, bypassLaunchQuiet: true);
            }
        }

        private static void SendInjectText(string text, string? chatId)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            if (_postJson == null)
                return;

            var payload = new
            {
                type = "continuum.inject_text",
                chatId = chatId,
                text = text
            };

            SendJson(payload);
        }

        private static string BuildInjectPayload(IReadOnlyList<AbyssSearchResult> results)
        {
            var sb = new StringBuilder();

            foreach (var result in results)
            {
                sb.AppendLine("[ABYSS RETRIEVAL]");
                sb.AppendLine($"Source: {result.Exchange.ChatId}");
                sb.AppendLine($"Lines: {FormatLineLocator(result.Exchange)}");
                sb.AppendLine(BuildExcerpt(result.Exchange, 1400));
                sb.AppendLine("[/ABYSS RETRIEVAL]");
                sb.AppendLine();
            }

            return sb.ToString().Trim();
        }

        private static object? BuildResultsPayload()
        {
            List<AbyssSearchResult> snapshot;
            string? query;
            string? queryOriginal;
            string? memoryRoot;
            string? generatedUtc;

            lock (Gate)
            {
                snapshot = _lastResults.ToList();
                query = _lastQuery;
                queryOriginal = _lastQueryOriginal;
                memoryRoot = _lastMemoryRoot;
                generatedUtc = _lastGeneratedUtc;
            }

            var results = new List<object>();
            for (int i = 0; i < snapshot.Count; i++)
            {
                var result = snapshot[i];
                var exchange = result.Exchange;
                var userLine = exchange.UserLines.FirstOrDefault();
                var assistantLineStart = exchange.AssistantLines.FirstOrDefault();
                var assistantLineEnd = exchange.AssistantLines.LastOrDefault();

                results.Add(new
                {
                    index = i + 1,
                    chatId = exchange.ChatId,
                    truthPath = exchange.TruthPath,
                    score = result.Score,
                    userText = exchange.UserText,
                    assistantText = exchange.AssistantText,
                    approxUserLine = userLine != null ? userLine.LineIndex + 1 : (int?)null,
                    approxAssistantLineStart = assistantLineStart != null ? assistantLineStart.LineIndex + 1 : (int?)null,
                    approxAssistantLineEnd = assistantLineEnd != null ? assistantLineEnd.LineIndex + 1 : (int?)null
                });
            }

            return new
            {
                type = "abyss.results",
                queryOriginal = queryOriginal ?? string.Empty,
                queryUsed = query ?? string.Empty,
                generatedUtc = generatedUtc ?? string.Empty,
                totalMatches = results.Count,
                memoryRoot = memoryRoot ?? string.Empty,
                results = results
            };
        }

        private static void TrackQuery(string queryUsed, string? queryOriginal, string memoryRoot)
        {
            lock (Gate)
            {
                _lastQuery = queryUsed;
                _lastQueryOriginal = queryOriginal ?? queryUsed;
                _lastMemoryRoot = memoryRoot;
                _lastGeneratedUtc = DateTime.UtcNow.ToString("O");
            }
        }

        private static string? WriteResultsFile(string? chatId, string query, IReadOnlyList<AbyssSearchResult> results)
        {
            var dir = ResolveSessionDir(chatId, results);
            if (string.IsNullOrWhiteSpace(dir))
                return null;

            var path = Path.Combine(dir, "Abyss.Results.txt");
            var sb = new StringBuilder();

            sb.AppendLine("Abyss Results");
            sb.AppendLine($"Query: {query}");
            sb.AppendLine($"Timestamp: {DateTime.UtcNow:O}");
            sb.AppendLine();

            for (int i = 0; i < results.Count; i++)
            {
                var result = results[i];
                sb.AppendLine($"[{i + 1}] Score: {result.Score}");
                sb.AppendLine($"Source: {result.Exchange.ChatId}");
                sb.AppendLine($"Truth: {Path.GetFileName(result.Exchange.TruthPath)}");
                sb.AppendLine($"Lines (approx): {FormatLineLocator(result.Exchange)}");
                sb.AppendLine("Excerpt:");
                sb.AppendLine(BuildExcerpt(result.Exchange, 1800));
                sb.AppendLine();
            }

            try
            {
                File.WriteAllText(path, sb.ToString().Trim());
                return path;
            }
            catch
            {
                return null;
            }
        }

        private static string BuildExcerpt(AbyssExchange exchange, int maxChars)
        {
            var lines = new List<string>();

            foreach (var line in exchange.UserLines)
                lines.Add($"U|{line.Text}");

            foreach (var line in exchange.AssistantLines)
                lines.Add($"A|{line.Text}");

            var excerpt = string.Join("\n", lines).Trim();
            if (excerpt.Length <= maxChars)
                return excerpt;

            return excerpt.Substring(0, maxChars).TrimEnd() + "…";
        }

        private static void ClearLastResults()
        {
            lock (Gate)
            {
                _lastResults = new List<AbyssSearchResult>();
            }
        }

        private static void SendJson(object payload)
        {
            if (_postJson == null)
                return;

            try
            {
                var json = JsonSerializer.Serialize(payload);
                _postJson?.Invoke(json);
            }
            catch { }
        }

        private static string FormatLineLocator(AbyssExchange exchange)
        {
            var parts = new List<string>();

            if (exchange.UserLines.Count > 0)
            {
                parts.Add(FormatLineRange('U', exchange.UserLines));
            }
            else
            {
                parts.Add("U@?");
            }

            if (exchange.AssistantLines.Count > 0)
            {
                parts.Add(FormatLineRange('A', exchange.AssistantLines));
            }
            else
            {
                parts.Add("A@?");
            }

            return string.Join(" ", parts);
        }

        private static string FormatLineRange(char role, List<AbyssTruthLine> lines)
        {
            var start = lines[0].LineIndex + 1;
            var end = lines[^1].LineIndex + 1;

            return start == end
                ? $"{role}@{start}"
                : $"{role}@{start}-{end}";
        }

        private static string? ResolveSessionDir(string? chatId, IReadOnlyList<AbyssSearchResult> results)
        {
            try
            {
                var resolved = SessionContext.ResolveChatId(chatId);
                if (SessionContext.IsValidChatId(resolved))
                    return TruthStorage.EnsureChatDir(resolved);
            }
            catch { }

            if (results.Count > 0)
            {
                try
                {
                    return Path.GetDirectoryName(results[0].Exchange.TruthPath);
                }
                catch { }
            }

            return null;
        }

        private static string ResolveMemoryRoot()
        {
            var root = ResolveProductRoot();
            return Path.Combine(root, "Memory", "Chats");
        }

        private static string ResolveProductRoot()
        {
            string bundleDir;
            try
            {
                var p = Environment.ProcessPath;
                bundleDir = !string.IsNullOrWhiteSpace(p)
                    ? (Path.GetDirectoryName(p) ?? AppContext.BaseDirectory)
                    : AppContext.BaseDirectory;
            }
            catch
            {
                bundleDir = AppContext.BaseDirectory;
            }

            if (Directory.Exists(Path.Combine(bundleDir, "Modules")) ||
                Directory.Exists(Path.Combine(bundleDir, "Dock")))
                return bundleDir;

            var productDir = Path.Combine(bundleDir, "PRODUCT");
            if (Directory.Exists(Path.Combine(productDir, "Modules")) ||
                Directory.Exists(Path.Combine(productDir, "Dock")))
                return productDir;

            var mainDir = Path.Combine(bundleDir, "MAIN");
            if (Directory.Exists(Path.Combine(mainDir, "Modules")))
            {
                var devProduct = Path.Combine(mainDir, "PRODUCT");
                return Directory.Exists(devProduct) ? devProduct : bundleDir;
            }

            return bundleDir;
        }
    }
}
