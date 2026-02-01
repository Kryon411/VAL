using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using VAL.Contracts;
using VAL.Continuum.Pipeline.Truth;
using VAL.Host.Security;
using VAL.Host.WebMessaging;

namespace VAL.Host.Abyss
{
    internal static class AbyssRuntime
    {
        private static readonly object Gate = new();
        private static IWebMessageSender? _messageSender;
        private static List<AbyssSearchResult> _lastResults = new();
        private static string? _lastQuery;
        private static string? _lastQueryOriginal;
        private static string? _lastGeneratedUtc;

        public static void Initialize(IWebMessageSender messageSender)
        {
            _messageSender = messageSender;
        }

        public static void InjectPrompt(string? chatId)
        {
            var prompt = "Abyss query: <keywords>";
            SendInjectText(prompt, chatId);
        }

        public static void Search(string? chatId, string query, int maxResults, string? queryOriginal = null, IReadOnlyCollection<string>? excludeFingerprints = null)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                ToastHub.TryShow(ToastKey.AbyssNoQuery, chatId: chatId);
                return;
            }

            maxResults = Math.Max(1, Math.Min(4, maxResults));
            ToastHub.TryShow(ToastKey.AbyssSearching, chatId: chatId);

            Task.Run(() =>
            {
                var memoryRoot = ResolveMemoryRoot();
                TrackQuery(query, queryOriginal);
                if (string.IsNullOrWhiteSpace(memoryRoot) || !Directory.Exists(memoryRoot))
                {
                    ClearLastResults();
                    ToastHub.TryShow(ToastKey.AbyssNoTruthLogs, chatId: chatId);
                    EmitResults(chatId);
                    return;
                }

                var results = AbyssSearch.Search(memoryRoot, query, maxResults, excludeFingerprints);
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

        public static void RetryLast(string? chatId, IReadOnlyCollection<string>? excludeFingerprints, int maxResults)
        {
            string? query;
            string? queryOriginal;

            lock (Gate)
            {
                query = _lastQuery;
                queryOriginal = _lastQueryOriginal;
            }

            if (string.IsNullOrWhiteSpace(query))
            {
                ToastHub.TryShow(ToastKey.AbyssNoQuery, chatId: chatId);
                return;
            }

            Search(chatId, query, maxResults, queryOriginal, excludeFingerprints);
        }

        public static void FetchLast(string? chatId, int count, bool inject)
        {
            ToastHub.TryShow(ToastKey.AbyssSearching, chatId: chatId);

            Task.Run(() =>
            {
                var memoryRoot = ResolveMemoryRoot();
                TrackQuery("(Last)", null);
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

            var first = indices.FirstOrDefault();
            if (first <= 0)
            {
                ToastHub.TryShow(ToastKey.AbyssNoSelection, chatId: chatId);
                return;
            }

            InjectResult(null, first, chatId);
        }

        public static void InjectResult(string? id, int? index, string? chatId = null)
        {
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

            AbyssSearchResult? selected = null;

            if (!string.IsNullOrWhiteSpace(id))
            {
                foreach (var result in snapshot)
                {
                    var fingerprint = AbyssSearch.BuildFingerprint(result.Exchange);
                    if (string.Equals(fingerprint, id, StringComparison.OrdinalIgnoreCase))
                    {
                        selected = result;
                        break;
                    }
                }
            }

            if (selected == null && index.HasValue)
            {
                var idx = index.Value;
                if (idx > 0 && idx <= snapshot.Count)
                    selected = snapshot[idx - 1];
            }

            if (selected == null)
            {
                ToastHub.TryShow(ToastKey.AbyssNoSelection, chatId: chatId);
                return;
            }

            var payload = BuildInjectPayload(selected, _lastQueryOriginal ?? _lastQuery ?? string.Empty);
            SendInjectText(payload, chatId);

            ToastHub.TryShow(
                ToastKey.AbyssInjected,
                chatId: chatId,
                titleOverride: "Abyss: injected result");
        }

        public static void EmitResults(string? chatId)
        {
            if (_messageSender == null)
                return;

            var payload = BuildResultsPayload();
            if (payload == null)
                return;

            SendEnvelope(WebMessageTypes.Event, "abyss.results", payload);
        }

        public static void ClearResults(string? chatId)
        {
            ClearLastResults();
        }

        public static void OpenSource(string? chatId)
        {
            if (string.IsNullOrWhiteSpace(chatId))
                return;

            if (!SafePathResolver.TryResolveChatTruthPath(ResolveProductRoot(), chatId, out var truthPath, out var chatDir))
                return;

            try
            {
                if (File.Exists(truthPath))
                {
                    Process.Start(new ProcessStartInfo { FileName = truthPath, UseShellExecute = true });
                    return;
                }

                if (Directory.Exists(chatDir))
                {
                    Process.Start(new ProcessStartInfo { FileName = chatDir, UseShellExecute = true });
                    return;
                }
            }
            catch
            {
                // ignore
            }

            ToastHub.TryShow(ToastKey.ActionUnavailable, chatId: chatId, bypassLaunchQuiet: true);
        }

        private static void SendInjectText(string text, string? chatId)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            if (_messageSender == null)
                return;

            SendEnvelope(WebMessageTypes.Command, WebCommandNames.ContinuumInjectText, new { chatId = chatId, text = text }, chatId);
        }

        private static string BuildInjectPayload(AbyssSearchResult result, string query)
        {
            var snippet = BuildSnippet(result.Exchange);
            var (startLine, endLine) = AbyssSearch.GetLineRange(result.Exchange);
            var rangeLabel = FormatLineRangeLabel(startLine, endLine);

            var sb = new StringBuilder();
            sb.AppendLine("ABYSS RECALL");
            sb.AppendLine($"Query: {query}".TrimEnd());
            sb.AppendLine("-----");
            sb.AppendLine(snippet);
            sb.AppendLine("-----");
            sb.AppendLine($"Source: {result.Exchange.ChatId} • Truth.log {rangeLabel}");

            return sb.ToString().Trim();
        }

        private static object? BuildResultsPayload()
        {
            List<AbyssSearchResult> snapshot;
            string? query;
            string? queryOriginal;
            string? generatedUtc;

            lock (Gate)
            {
                snapshot = _lastResults.ToList();
                query = _lastQuery;
                queryOriginal = _lastQueryOriginal;
                generatedUtc = _lastGeneratedUtc;
            }

            var results = new List<object>();
            for (int i = 0; i < snapshot.Count; i++)
            {
                var result = snapshot[i];
                var exchange = result.Exchange;
                var (startLine, endLine) = AbyssSearch.GetLineRange(exchange);
                var fingerprint = AbyssSearch.BuildFingerprint(exchange);
                var snippet = BuildSnippet(exchange);
                var preview = BuildPreview(snippet);
                var title = BuildTitle(exchange);

                results.Add(new
                {
                    index = i + 1,
                    chatId = exchange.ChatId,
                    score = result.Score,
                    id = fingerprint,
                    fingerprint = fingerprint,
                    title = title,
                    preview = preview,
                    snippet = snippet,
                    startLine = startLine,
                    endLine = endLine
                });
            }

            return new
            {
                type = "abyss.results",
                queryOriginal = queryOriginal ?? string.Empty,
                queryUsed = query ?? string.Empty,
                generatedUtc = generatedUtc ?? string.Empty,
                totalMatches = results.Count,
                resultCount = results.Count,
                results = results
            };
        }

        private static string BuildSnippet(AbyssExchange exchange)
        {
            if (exchange == null)
                return string.Empty;

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(exchange.UserText))
                parts.Add(exchange.UserText.Trim());
            if (!string.IsNullOrWhiteSpace(exchange.AssistantText))
                parts.Add(exchange.AssistantText.Trim());

            return string.Join("\n\n", parts).Trim();
        }

        private static string BuildPreview(string snippet)
        {
            if (string.IsNullOrWhiteSpace(snippet))
                return string.Empty;

            var lines = snippet.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            return string.Join("\n", lines.Take(3)).Trim();
        }

        private static string BuildTitle(AbyssExchange exchange)
        {
            if (exchange == null)
                return "Abyss Match";

            foreach (var line in exchange.AssistantLines)
            {
                var text = line.Text?.Trim();
                if (!string.IsNullOrWhiteSpace(text))
                    return text;
            }

            foreach (var line in exchange.UserLines)
            {
                var text = line.Text?.Trim();
                if (!string.IsNullOrWhiteSpace(text))
                    return text;
            }

            return "Abyss Match";
        }

        private static string FormatLineRangeLabel(int startLine, int endLine)
        {
            if (startLine <= 0 || endLine <= 0)
                return "L?";

            return startLine == endLine
                ? $"L{startLine}"
                : $"L{startLine}–L{endLine}";
        }

        private static void TrackQuery(string queryUsed, string? queryOriginal)
        {
            lock (Gate)
            {
                _lastQuery = queryUsed;
                _lastQueryOriginal = queryOriginal ?? queryUsed;
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

        private static void SendEnvelope(string type, string name, object payload, string? chatId = null)
        {
            if (_messageSender == null)
                return;

            try
            {
                _messageSender.Send(new MessageEnvelope
                {
                    Type = type,
                    Name = name,
                    ChatId = chatId,
                    Payload = JsonSerializer.SerializeToElement(payload)
                });
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
