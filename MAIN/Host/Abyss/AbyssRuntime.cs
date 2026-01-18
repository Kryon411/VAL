using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using VAL.Continuum.Pipeline;
using VAL.Continuum.Pipeline.Inject;
using VAL.Continuum.Pipeline.Truth;

namespace VAL.Host.Abyss
{
    internal static class AbyssRuntime
    {
        private const int DefaultLimit = 10;
        private const int MaxExcerptLength = 1800;
        private const int MaxInjectResults = 3;
        private const int PreviewExcerptLength = 360;

        private static readonly object Sync = new();
        private static readonly Dictionary<string, AbyssResultSet> ResultsByChat =
            new(StringComparer.OrdinalIgnoreCase);

        private sealed class AbyssResultSet
        {
            public string QueryOriginal { get; init; } = string.Empty;
            public string QueryUsed { get; init; } = string.Empty;
            public DateTime GeneratedUtc { get; init; }
            public IReadOnlyList<AbyssSearch.AbyssSearchResult> Results { get; init; } =
                Array.Empty<AbyssSearch.AbyssSearchResult>();
            public string ResultsPath { get; init; } = string.Empty;
        }

        public static void Search(string? chatId, string? queryOriginal, string[]? excludeChatIds, int? maxResults)
        {
            var resolvedChatId = ResolveChatId(chatId);
            var original = (queryOriginal ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(original))
            {
                ToastHub.TryShow(
                    ToastKey.AbyssQueryPrompt,
                    titleOverride: "Abyss: enter a query to search.",
                    bypassLaunchQuiet: true);
                StoreResults(resolvedChatId, string.Empty, string.Empty, Array.Empty<AbyssSearch.AbyssSearchResult>(), string.Empty);
                SendResults(resolvedChatId);
                return;
            }

            var queryUsed = BuildQueryUsed(original);
            ToastHub.TryShow(
                ToastKey.AbyssSearching,
                titleOverride: "Abyss: searching...",
                bypassLaunchQuiet: true,
                groupKeyOverride: "abyss",
                replaceGroupOverride: true);

            var chatsRoot = GetChatsRoot(resolvedChatId);
            if (string.IsNullOrWhiteSpace(chatsRoot) || !Directory.Exists(chatsRoot))
            {
                ToastHub.TryShow(
                    ToastKey.AbyssNoTruthLogs,
                    titleOverride: "Abyss: no Truth logs found.",
                    bypassLaunchQuiet: true);
                return;
            }

            var limit = maxResults.HasValue ? Math.Clamp(maxResults.Value, 1, 50) : DefaultLimit;
            var results = AbyssSearch.Search(chatsRoot, queryUsed, limit);

            if (excludeChatIds != null && excludeChatIds.Length > 0)
            {
                var exclude = new HashSet<string>(excludeChatIds.Where(x => !string.IsNullOrWhiteSpace(x)), StringComparer.OrdinalIgnoreCase);
                results = results.Where(r => !exclude.Contains(r.SessionId)).ToList();
            }

            var resultsPath = WriteResults(resolvedChatId, original, queryUsed, results);
            StoreResults(resolvedChatId, original, queryUsed, results, resultsPath);
            SendResults(resolvedChatId);

            if (results.Count == 0)
            {
                ToastHub.TryShow(
                    ToastKey.AbyssNoMatches,
                    titleOverride: "Abyss: no matches.",
                    bypassLaunchQuiet: true,
                    groupKeyOverride: "abyss",
                    replaceGroupOverride: true);
            }
            else
            {
                ToastHub.TryShow(
                    ToastKey.AbyssMatches,
                    titleOverride: $"Abyss: {results.Count} match(es).",
                    bypassLaunchQuiet: true,
                    groupKeyOverride: "abyss",
                    replaceGroupOverride: true);
            }

            if (!string.IsNullOrWhiteSpace(resultsPath))
            {
                ToastHub.TryShow(
                    ToastKey.AbyssResultsWritten,
                    titleOverride: "Abyss: wrote Abyss.Results.txt.",
                    bypassLaunchQuiet: true,
                    groupKeyOverride: "abyss",
                    replaceGroupOverride: false);
            }
        }

        public static void SendResults(string? chatId)
        {
            var resolvedChatId = ResolveChatId(chatId);
            if (!TryGetResultSet(resolvedChatId, out var resultSet))
            {
                resultSet = new AbyssResultSet
                {
                    QueryOriginal = string.Empty,
                    QueryUsed = string.Empty,
                    GeneratedUtc = DateTime.UtcNow,
                    Results = Array.Empty<AbyssSearch.AbyssSearchResult>(),
                    ResultsPath = string.Empty
                };
            }

            var payload = BuildResultsEventPayload(resolvedChatId, resultSet);
            TryPostResults(payload);
        }

        public static void InjectResults(string? chatId, string indicesRaw)
        {
            var resolvedChatId = ResolveChatId(chatId);
            if (string.IsNullOrWhiteSpace(indicesRaw))
                return;

            if (!TryGetResultSet(resolvedChatId, out var resultSet))
            {
                ToastHub.TryShow(
                    ToastKey.AbyssNoMatches,
                    titleOverride: "Abyss: no cached results to inject.",
                    bypassLaunchQuiet: true);
                return;
            }

            var indices = ParseIndices(indicesRaw, resultSet.Results.Count, MaxInjectResults).ToList();
            if (indices.Count == 0)
            {
                ToastHub.TryShow(
                    ToastKey.AbyssNoMatches,
                    titleOverride: "Abyss: invalid result number.",
                    bypassLaunchQuiet: true);
                return;
            }

            var selected = indices.Select(i => resultSet.Results[i - 1]).ToList();
            var payload = BuildInjectionPayload(selected);
            QueueInject(resolvedChatId, payload, "Abyss.Retrieval.txt");

            ToastHub.TryShow(
                ToastKey.AbyssInjected,
                titleOverride: $"Abyss: injected result #{indices.First()}.",
                bypassLaunchQuiet: true,
                groupKeyOverride: "abyss",
                replaceGroupOverride: true);
        }

        public static void InjectLast(string? chatId)
        {
            var resolvedChatId = ResolveChatId(chatId);
            var chatsRoot = GetChatsRoot(resolvedChatId);
            if (string.IsNullOrWhiteSpace(chatsRoot) || !Directory.Exists(chatsRoot))
            {
                ToastHub.TryShow(
                    ToastKey.AbyssNoTruthLogs,
                    titleOverride: "Abyss: no Truth logs found.",
                    bypassLaunchQuiet: true);
                return;
            }

            var latestTruth = FindLatestTruthLog(chatsRoot);
            if (string.IsNullOrWhiteSpace(latestTruth))
            {
                ToastHub.TryShow(
                    ToastKey.AbyssNoTruthLogs,
                    titleOverride: "Abyss: no Truth logs found.",
                    bypassLaunchQuiet: true);
                return;
            }

            var results = AbyssSearch.TakeLastExchanges(latestTruth, 2).ToList();
            if (results.Count == 0)
            {
                ToastHub.TryShow(
                    ToastKey.AbyssNoMatches,
                    titleOverride: "Abyss: no exchanges found.",
                    bypassLaunchQuiet: true);
                return;
            }

            var payload = BuildInjectionPayload(results);
            QueueInject(resolvedChatId, payload, "Abyss.Last.txt");

            ToastHub.TryShow(
                ToastKey.AbyssInjected,
                titleOverride: "Abyss: injected last exchange.",
                bypassLaunchQuiet: true,
                groupKeyOverride: "abyss",
                replaceGroupOverride: true);
        }

        public static void OpenSource(string? chatId, string? truthPath)
        {
            var resolvedChatId = ResolveChatId(chatId);
            if (string.IsNullOrWhiteSpace(resolvedChatId))
                return;

            try
            {
                if (!string.IsNullOrWhiteSpace(truthPath) && File.Exists(truthPath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{truthPath}\"",
                        UseShellExecute = true
                    });
                    return;
                }

                var dir = TruthStorage.EnsureChatDir(resolvedChatId);
                Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
            }
            catch
            {
                ToastHub.TryShow(ToastKey.ActionUnavailable, bypassLaunchQuiet: true);
            }
        }

        private static string ResolveChatId(string? chatId)
        {
            var resolved = SessionContext.ResolveChatId(chatId);
            if (!string.IsNullOrWhiteSpace(resolved))
                return resolved;

            return "session-" + DateTime.UtcNow.Ticks;
        }

        private static string GetChatsRoot(string chatId)
        {
            try
            {
                var chatDir = TruthStorage.GetChatDir(chatId);
                var parent = Directory.GetParent(chatDir);
                return parent?.FullName ?? chatDir;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string BuildQueryUsed(string queryOriginal)
        {
            if (string.IsNullOrWhiteSpace(queryOriginal))
                return string.Empty;

            var tokens = Regex.Matches(queryOriginal, "[A-Za-z0-9]+")
                .Select(m => m.Value)
                .Where(t => t.Length > 1)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (tokens.Count == 0)
                return queryOriginal.Trim();

            return string.Join(" ", tokens);
        }

        private static string UnescapeTruthText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            return text
                .Replace("\\r\\n", "\n")
                .Replace("\\r", "\n")
                .Replace("\\n", "\n")
                .Replace("\\t", "\t");
        }

        private static void TryPostResults(object payload)
        {
            try
            {
                var post = ContinuumHost.PostToWebJson;
                if (post == null) return;
                var json = JsonSerializer.Serialize(payload);
                post(json);
            }
            catch { }
        }

        private static object BuildResultsEventPayload(string chatId, AbyssResultSet resultSet)
        {
            var results = new List<object>();
            for (var i = 0; i < resultSet.Results.Count; i++)
            {
                var r = resultSet.Results[i];
                results.Add(new
                {
                    index = i + 1,
                    score = r.Score,
                    chatId = r.SessionFolder,
                    truthPath = r.TruthPath,
                    approxUserLine = r.UserLineNumber,
                    approxAssistantLineStart = r.AssistantLineStart,
                    approxAssistantLineEnd = r.AssistantLineEnd,
                    userText = ClampText(UnescapeTruthText(r.UserText ?? string.Empty), PreviewExcerptLength),
                    assistantText = ClampText(UnescapeTruthText(string.Join(Environment.NewLine, r.AssistantLines ?? new List<string>())), PreviewExcerptLength)
                });
            }

            return new
            {
                type = "abyss.results",
                queryOriginal = resultSet.QueryOriginal,
                queryUsed = resultSet.QueryUsed,
                generatedUtc = resultSet.GeneratedUtc.ToString("O"),
                totalMatches = resultSet.Results.Count,
                memoryRoot = GetChatsRoot(chatId),
                results
            };
        }

        private static void QueueInject(string chatId, string text, string sourceFile)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            var seed = new EssenceInjectController.InjectSeed
            {
                ChatId = chatId,
                Mode = "Abyss",
                EssenceText = text.Trim(),
                OpenNewChat = false,
                SourceFileName = sourceFile,
                EssenceFileName = sourceFile
            };

            EssenceInjectQueue.Enqueue(seed);
        }

        private static string WriteResults(string chatId, string queryOriginal, string queryUsed, IReadOnlyList<AbyssSearch.AbyssSearchResult> results)
        {
            var dir = TruthStorage.EnsureChatDir(chatId);
            if (string.IsNullOrWhiteSpace(dir))
                return string.Empty;

            var path = Path.Combine(dir, "Abyss.Results.txt");
            var sb = new StringBuilder();
            sb.AppendLine("[ABYSS RESULTS]");
            sb.AppendLine($"QueryOriginal: {queryOriginal}");
            sb.AppendLine($"QueryUsed: {queryUsed}");
            sb.AppendLine($"Generated: {DateTime.UtcNow:O}");
            sb.AppendLine("Scope: All Chats");
            sb.AppendLine($"Matches: {results.Count}");
            sb.AppendLine();

            for (var i = 0; i < results.Count; i++)
            {
                var result = results[i];
                sb.AppendLine($"Result #{i + 1} (Score: {result.Score})");
                sb.AppendLine($"ChatId: {result.SessionFolder}");
                sb.AppendLine("Truth: Truth.log");
                sb.AppendLine($"Lines (approx): U@{FormatLine(result.UserLineNumber)} A@{FormatLineRange(result.AssistantLineStart, result.AssistantLineEnd)}");
                sb.AppendLine("Excerpt:");
                sb.AppendLine(ClampText(BuildExchangeText(result), MaxExcerptLength));
                sb.AppendLine("[/RESULT]");
                sb.AppendLine();
            }

            sb.AppendLine("[/ABYSS RESULTS]");

            try
            {
                File.WriteAllText(path, sb.ToString());
            }
            catch
            {
                return string.Empty;
            }

            return path;
        }

        private static void StoreResults(string chatId, string queryOriginal, string queryUsed, IReadOnlyList<AbyssSearch.AbyssSearchResult> results, string path)
        {
            lock (Sync)
            {
                ResultsByChat[chatId] = new AbyssResultSet
                {
                    QueryOriginal = queryOriginal,
                    QueryUsed = queryUsed,
                    GeneratedUtc = DateTime.UtcNow,
                    Results = results,
                    ResultsPath = path
                };
            }
        }

        private static bool TryGetResultSet(string chatId, out AbyssResultSet resultSet)
        {
            lock (Sync)
            {
                return ResultsByChat.TryGetValue(chatId, out resultSet!);
            }
        }

        private static string BuildInjectionPayload(IReadOnlyList<AbyssSearch.AbyssSearchResult> results)
        {
            var sb = new StringBuilder();
            sb.AppendLine("[ABYSS RETRIEVAL]");
            for (var i = 0; i < results.Count; i++)
            {
                var result = results[i];
                if (i > 0) sb.AppendLine("---");
                sb.AppendLine($"Source: {result.SessionFolder}");
                sb.AppendLine($"Approx Lines: U@{FormatLine(result.UserLineNumber)} A@{FormatLineRange(result.AssistantLineStart, result.AssistantLineEnd)}");
                sb.AppendLine(ClampText(BuildExchangeText(result), MaxExcerptLength));
            }
            sb.AppendLine("[/ABYSS RETRIEVAL]");
            return sb.ToString();
        }

        private static string BuildExchangeText(AbyssSearch.AbyssSearchResult result)
        {
            var lines = new List<string>();
            if (!string.IsNullOrWhiteSpace(result.UserText))
                lines.Add($"U|{UnescapeTruthText(result.UserText)}");

            if (result.AssistantLines != null && result.AssistantLines.Count > 0)
            {
                foreach (var line in result.AssistantLines)
                    lines.Add($"A|{UnescapeTruthText(line)}");
            }

            return lines.Count > 0 ? string.Join(Environment.NewLine, lines) : "(no exchange text)";
        }

        private static string ClampText(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text) || maxChars <= 0)
                return text;

            if (text.Length <= maxChars)
                return text;

            return text.Substring(0, maxChars).TrimEnd() + "…";
        }

        private static string FormatLine(int lineNumber)
        {
            return lineNumber > 0 ? lineNumber.ToString() : "n/a";
        }

        private static string FormatLineRange(int start, int end)
        {
            if (start <= 0 && end <= 0)
                return "n/a";

            if (start > 0 && end > 0 && end != start)
                return $"{start}-{end}";

            return (start > 0 ? start : end).ToString();
        }

        private static IEnumerable<int> ParseIndices(string raw, int max, int limit)
        {
            if (string.IsNullOrWhiteSpace(raw))
                yield break;

            var parts = raw.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                if (!int.TryParse(part.Trim(), out var idx))
                    continue;

                if (idx < 1 || idx > max)
                    continue;

                yield return idx;
                limit--;
                if (limit <= 0)
                    yield break;
            }
        }

        private static string FindLatestTruthLog(string chatsRoot)
        {
            try
            {
                var latest = Directory.EnumerateFiles(chatsRoot, TruthStorage.TruthFileName, SearchOption.AllDirectories)
                    .Select(path => new
                    {
                        Path = path,
                        LastWriteUtc = SafeGetLastWriteUtc(path)
                    })
                    .OrderByDescending(x => x.LastWriteUtc)
                    .FirstOrDefault();

                return latest?.Path ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static DateTime SafeGetLastWriteUtc(string path)
        {
            try { return File.GetLastWriteTimeUtc(path); } catch { return DateTime.MinValue; }
        }
    }
}
