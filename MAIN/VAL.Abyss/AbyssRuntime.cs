using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using VAL.Continuum.Pipeline.Truth;
using VAL.Contracts;
using VAL.Host;
using VAL.Host.Security;
using VAL.Host.Services;
using VAL.Host.WebMessaging;

namespace VAL.Host.Abyss
{
    internal enum AbyssToastKind
    {
        NoQuery,
        Searching,
        NoTruthLogs,
        NoMatches,
        Matches,
        ResultsWritten,
        Injected,
        NoSelection,
        ActionUnavailable
    }

    public sealed class AbyssRuntime : IDisposable
    {
        private readonly object _gate = new();
        private static readonly int[] DefaultInjectIndices = { 1 };
        private static readonly string[] SnippetLineSeparators = { "\r\n", "\n" };
        private readonly AbyssSearchService _abyssSearch;
        private readonly IWebMessageSender _messageSender;
        private readonly IToastHub _toastHub;
        private readonly ISessionContext _sessionContext;
        private readonly IAppPaths _appPaths;
        private readonly ITruthStore _truthStore;
        private readonly IProcessLauncher _processLauncher;
        private readonly IBackgroundTaskSupervisor _backgroundTasks;
        private readonly ILog _log;
        private readonly IUiThread _uiThread;
        private long _searchGeneration;
        private bool _disposed;
        private List<AbyssSearchResult> _lastResults = new();
        private string? _lastQuery;
        private string? _lastQueryOriginal;
        private string? _lastGeneratedUtc;
        private int? _lastRecentCount;
        private CancellationTokenSource? _searchCts;

        public AbyssRuntime(
            AbyssSearchService abyssSearch,
            IWebMessageSender messageSender,
            IToastHub toastHub,
            ISessionContext sessionContext,
            IAppPaths appPaths,
            ITruthStore truthStore,
            IProcessLauncher processLauncher,
            IBackgroundTaskSupervisor backgroundTasks,
            ILog log,
            IUiThread uiThread)
        {
            _abyssSearch = abyssSearch ?? throw new ArgumentNullException(nameof(abyssSearch));
            _messageSender = messageSender ?? throw new ArgumentNullException(nameof(messageSender));
            _toastHub = toastHub ?? throw new ArgumentNullException(nameof(toastHub));
            _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
            _appPaths = appPaths ?? throw new ArgumentNullException(nameof(appPaths));
            _truthStore = truthStore ?? throw new ArgumentNullException(nameof(truthStore));
            _processLauncher = processLauncher ?? throw new ArgumentNullException(nameof(processLauncher));
            _backgroundTasks = backgroundTasks ?? throw new ArgumentNullException(nameof(backgroundTasks));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _uiThread = uiThread ?? throw new ArgumentNullException(nameof(uiThread));
        }

        public void InjectPrompt(string? chatId)
        {
            var prompt = "Abyss query: <keywords>";
            SendInjectText(prompt, chatId);
        }

        public void Search(string? chatId, string query, int maxResults, string? queryOriginal = null,
            IReadOnlyCollection<string>? excludeFingerprints = null, string? requestId = null)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                ShowToast(AbyssToastKind.NoQuery, chatId);
                return;
            }

            maxResults = Math.Max(1, Math.Min(4, maxResults));
            ShowToast(AbyssToastKind.Searching, chatId);

            var (generation, token) = BeginSearchCancellation();

            _backgroundTasks.Run(
                "Abyss search",
                cancellationToken => ExecuteSearchAsync(chatId, query, maxResults, queryOriginal,
                    excludeFingerprints, generation, requestId, cancellationToken),
                _ => HandleBackgroundFailure(chatId, generation),
                token);
        }

        private async Task ExecuteSearchAsync(
            string? chatId,
            string query,
            int maxResults,
            string? queryOriginal,
            IReadOnlyCollection<string>? excludeFingerprints,
            long generation,
            string? requestId,
            CancellationToken cancellationToken)
        {
            var memoryRoot = _appPaths.MemoryChatsRoot;
            if (string.IsNullOrWhiteSpace(memoryRoot) || !Directory.Exists(memoryRoot))
            {
                await CommitResultsAsync(chatId, query, queryOriginal, new(), generation,
                    requestId, cancellationToken, noTruthLogs: true).ConfigureAwait(false);
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();

            var results = _abyssSearch.Search(
                memoryRoot,
                query,
                maxResults,
                excludeFingerprints,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            await CommitResultsAsync(chatId, query, queryOriginal, results, generation,
                requestId, cancellationToken).ConfigureAwait(false);
        }

        public void RetryLast(string? chatId, IReadOnlyCollection<string>? excludeFingerprints, int maxResults,
            string? requestId = null)
        {
            string? query;
            string? queryOriginal;
            int? recentCount;

            lock (_gate)
            {
                query = _lastQuery;
                queryOriginal = _lastQueryOriginal;
                recentCount = _lastRecentCount;
            }

            if (recentCount.HasValue)
            {
                FetchLast(chatId, recentCount.Value, inject: false, requestId);
                return;
            }

            if (string.IsNullOrWhiteSpace(query))
            {
                ShowToast(AbyssToastKind.NoQuery, chatId);
                return;
            }

            Search(chatId, query, maxResults, queryOriginal, excludeFingerprints, requestId);
        }

        public void FetchLast(string? chatId, int count, bool inject, string? requestId = null)
        {
            ShowToast(AbyssToastKind.Searching, chatId);
            var (generation, token) = BeginSearchCancellation();

            _backgroundTasks.Run(
                "Abyss recent recall",
                cancellationToken => ExecuteFetchLastAsync(chatId, count, inject, generation, requestId, cancellationToken),
                _ => HandleBackgroundFailure(chatId, generation),
                token);
        }

        private async Task ExecuteFetchLastAsync(
            string? chatId,
            int count,
            bool inject,
            long generation,
            string? requestId,
            CancellationToken cancellationToken)
        {
            var memoryRoot = _appPaths.MemoryChatsRoot;
            if (string.IsNullOrWhiteSpace(memoryRoot) || !Directory.Exists(memoryRoot))
            {
                await CommitResultsAsync(chatId, "(Last)", null, new(), generation,
                    requestId, cancellationToken, noTruthLogs: true, recentCount: count).ConfigureAwait(false);
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var exchanges = _abyssSearch.GetLastFromMostRecent(memoryRoot, count, cancellationToken);
            var results = exchanges
                .Select(exchange => new AbyssSearchResult { Exchange = exchange, Score = 0 })
                .ToList();
            cancellationToken.ThrowIfCancellationRequested();

            await CommitResultsAsync(chatId, "(Last)", null, results, generation,
                requestId, cancellationToken, inject: inject, recentCount: count).ConfigureAwait(false);
        }

        private Task CommitResultsAsync(string? chatId, string query, string? original,
            List<AbyssSearchResult> results, long generation, string? requestId, CancellationToken token,
            bool noTruthLogs = false, bool inject = false, int? recentCount = null)
        {
            // Publication, Clear, and new requests share the UI ordering boundary.
            return _uiThread.InvokeAsync(() =>
            {
                lock (_gate)
                {
                    if (_disposed || token.IsCancellationRequested || generation != _searchGeneration)
                        return;
                    _lastResults = results;
                    _lastQuery = query;
                    _lastQueryOriginal = original ?? query;
                    _lastGeneratedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                    _lastRecentCount = recentCount;
                    ShowToast(noTruthLogs ? AbyssToastKind.NoTruthLogs :
                        results.Count == 0 ? AbyssToastKind.NoMatches : AbyssToastKind.Matches,
                        chatId, results.Count == 0 ? null : $"Abyss: {results.Count} matches");
                    if (results.Count > 0 && !string.IsNullOrWhiteSpace(WriteResultsFile(chatId, query, results)))
                        ShowToast(AbyssToastKind.ResultsWritten, chatId);
                    EmitResults(chatId, requestId);
                    if (inject && results.Count > 0) InjectResults(DefaultInjectIndices, chatId);
                }
            });
        }

        public void InjectResults(IReadOnlyList<int> indices, string? chatId = null)
        {
            if (indices == null || indices.Count == 0)
            {
                ShowToast(AbyssToastKind.NoSelection, chatId);
                return;
            }

            var first = indices[0];
            if (first <= 0)
            {
                ShowToast(AbyssToastKind.NoSelection, chatId);
                return;
            }

            InjectResult(null, first, chatId);
        }

        public void InjectResult(string? id, int? index, string? chatId = null)
        {
            List<AbyssSearchResult> snapshot;
            string query;
            lock (_gate)
            {
                snapshot = _lastResults.ToList();
                query = _lastQueryOriginal ?? _lastQuery ?? string.Empty;
            }

            if (snapshot.Count == 0)
            {
                ShowToast(AbyssToastKind.NoMatches, chatId);
                return;
            }

            AbyssSearchResult? selected = null;

            if (!string.IsNullOrWhiteSpace(id))
            {
                foreach (var result in snapshot)
                {
                    var fingerprint = AbyssSearchService.BuildFingerprint(result.Exchange);
                    if (string.Equals(fingerprint, id, StringComparison.OrdinalIgnoreCase))
                    {
                        selected = result;
                        break;
                    }
                }
            }

            if (selected == null && string.IsNullOrWhiteSpace(id) && index.HasValue)
            {
                var idx = index.Value;
                if (idx > 0 && idx <= snapshot.Count)
                    selected = snapshot[idx - 1];
            }

            if (selected == null)
            {
                ShowToast(AbyssToastKind.NoSelection, chatId);
                return;
            }

            var payload = BuildInjectPayload(selected, query);
            SendInjectText(payload, chatId);

            ShowToast(AbyssToastKind.Injected, chatId, "Abyss: injected result");
        }

        public void EmitResults(string? chatId, string? requestId = null, bool cleared = false)
        {
            var payload = BuildResultsPayload(requestId, cleared);
            if (payload == null)
                return;

            SendEnvelope(WebMessageTypes.Event, "abyss.results", payload, chatId);
        }

        public void ClearResults(string? chatId, string? requestId = null)
        {
            _uiThread.Invoke(() =>
            {
                lock (_gate)
                {
                    CancelSearch();
                    ClearLastResults();
                    EmitResults(chatId, requestId, cleared: true);
                }
            });
        }

        public void OpenSource(string? chatId)
        {
            if (string.IsNullOrWhiteSpace(chatId))
                return;

            if (!SafePathResolver.TryResolveChatTruthPath(_appPaths.MemoryChatsRoot, chatId, out var truthPath, out var chatDir))
                return;

            try
            {
                if (File.Exists(truthPath))
                {
                    _processLauncher.OpenPath(truthPath);
                    return;
                }

                if (Directory.Exists(chatDir))
                {
                    _processLauncher.OpenFolder(chatDir);
                    return;
                }
            }
            catch
            {
                // ignore
            }

            ShowToast(AbyssToastKind.ActionUnavailable, chatId, bypassLaunchQuiet: true);
        }

        private void SendInjectText(string text, string? chatId)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            SendEnvelope(WebMessageTypes.Command, WebCommandNames.ContinuumInjectText, new { chatId = chatId, text = text }, chatId);
        }

        private static string BuildInjectPayload(AbyssSearchResult result, string query)
        {
            var snippet = BuildSnippet(result.Exchange);
            var (startLine, endLine) = AbyssSearchService.GetLineRange(result.Exchange);
            var rangeLabel = FormatLineRangeLabel(startLine, endLine);

            var sb = new StringBuilder();
            sb.AppendLine("ABYSS RECALL");
            sb.AppendLine(FormattableString.Invariant($"Query: {query}").TrimEnd());
            sb.AppendLine("-----");
            sb.AppendLine(snippet);
            sb.AppendLine("-----");
            sb.AppendLine(FormattableString.Invariant($"Source: {result.Exchange.ChatId} • Truth.log {rangeLabel}"));

            return sb.ToString().Trim();
        }

        private object? BuildResultsPayload(string? requestId, bool cleared)
        {
            List<AbyssSearchResult> snapshot;
            string? query;
            string? queryOriginal;
            string? generatedUtc;

            lock (_gate)
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
                var (startLine, endLine) = AbyssSearchService.GetLineRange(exchange);
                var fingerprint = AbyssSearchService.BuildFingerprint(exchange);
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
                requestId = requestId,
                cleared = cleared,
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

            var lines = snippet.Split(SnippetLineSeparators, StringSplitOptions.None);
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

        private (long Generation, CancellationToken Token) BeginSearchCancellation()
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                CancelSearch();
                _searchCts = new CancellationTokenSource();
                return (_searchGeneration, _searchCts.Token);
            }
        }

        private void CancelSearch()
        {
            _searchGeneration++;
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searchCts = null;
        }

        private string? WriteResultsFile(string? chatId, string query, List<AbyssSearchResult> results)
        {
            var dir = ResolveSessionDir(chatId, results);
            if (string.IsNullOrWhiteSpace(dir))
                return null;

            var path = Path.Combine(dir, "Abyss.Results.txt");
            var sb = new StringBuilder();

            sb.AppendLine("Abyss Results");
            sb.AppendLine(FormattableString.Invariant($"Query: {query}"));
            sb.AppendLine(FormattableString.Invariant($"Timestamp: {DateTime.UtcNow:O}"));
            sb.AppendLine();

            for (int i = 0; i < results.Count; i++)
            {
                var result = results[i];
                sb.AppendLine(FormattableString.Invariant($"[{i + 1}] Score: {result.Score}"));
                sb.AppendLine(FormattableString.Invariant($"Source: {result.Exchange.ChatId}"));
                sb.AppendLine(FormattableString.Invariant($"Truth: {Path.GetFileName(result.Exchange.TruthPath)}"));
                sb.AppendLine(FormattableString.Invariant($"Lines (approx): {FormatLineLocator(result.Exchange)}"));
                sb.AppendLine("Excerpt:");
                sb.AppendLine(BuildExcerpt(result.Exchange, 1800));
                sb.AppendLine();
            }

            var temp = path + ".tmp." + Guid.NewGuid().ToString("N");
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                           4096, FileOptions.WriteThrough))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                {
                    writer.Write(sb.ToString().Trim());
                    writer.Flush();
                    stream.Flush(flushToDisk: true);
                }
                File.Move(temp, path, overwrite: true);
                return path;
            }
            catch
            {
                return null;
            }
            finally
            {
                try { File.Delete(temp); } catch { }
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

        private void ClearLastResults()
        {
            lock (_gate)
            {
                _lastResults = new List<AbyssSearchResult>();
                _lastQuery = null;
                _lastQueryOriginal = null;
                _lastGeneratedUtc = null;
                _lastRecentCount = null;
            }
        }

        private void HandleBackgroundFailure(string? chatId, long generation)
        {
            _uiThread.Invoke(() =>
            {
                lock (_gate)
                {
                    if (_disposed || generation != _searchGeneration) return;
                    _log.Warn(nameof(AbyssRuntime), "Abyss background operation failed.");
                    ShowToast(AbyssToastKind.ActionUnavailable, chatId, bypassLaunchQuiet: true);
                }
            });
        }

        private void ShowToast(
            AbyssToastKind kind,
            string? chatId = null,
            string? titleOverride = null,
            bool bypassLaunchQuiet = false)
        {
            switch (kind)
            {
                case AbyssToastKind.NoQuery:
                    _toastHub.TryShow(ToastKey.AbyssNoQuery, chatId: chatId);
                    break;
                case AbyssToastKind.Searching:
                    _toastHub.TryShow(ToastKey.AbyssSearching, chatId: chatId);
                    break;
                case AbyssToastKind.NoTruthLogs:
                    _toastHub.TryShow(ToastKey.AbyssNoTruthLogs, chatId: chatId);
                    break;
                case AbyssToastKind.NoMatches:
                    _toastHub.TryShow(ToastKey.AbyssNoMatches, chatId: chatId);
                    break;
                case AbyssToastKind.Matches:
                    _toastHub.TryShow(ToastKey.AbyssMatches, chatId: chatId, titleOverride: titleOverride);
                    break;
                case AbyssToastKind.ResultsWritten:
                    _toastHub.TryShow(ToastKey.AbyssResultsWritten, chatId: chatId);
                    break;
                case AbyssToastKind.Injected:
                    _toastHub.TryShow(ToastKey.AbyssInjected, chatId: chatId, titleOverride: titleOverride);
                    break;
                case AbyssToastKind.NoSelection:
                    _toastHub.TryShow(ToastKey.AbyssNoSelection, chatId: chatId);
                    break;
                case AbyssToastKind.ActionUnavailable:
                    _toastHub.TryShow(ToastKey.ActionUnavailable, chatId: chatId, bypassLaunchQuiet: bypassLaunchQuiet);
                    break;
            }
        }

        private void SendEnvelope(string type, string name, object payload, string? chatId = null)
        {
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

        private string? ResolveSessionDir(string? chatId, List<AbyssSearchResult> results)
        {
            try
            {
                var resolved = _sessionContext.ResolveChatId(chatId);
                if (_sessionContext.IsValidChatId(resolved))
                    return _truthStore.EnsureChatDir(resolved);
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

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                CancelSearch();
                ClearLastResults();
                _abyssSearch.ClearCache();
            }
        }
    }
}
