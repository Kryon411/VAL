using System;
using System.Collections.Generic;
using System.Text.Json;
using VAL.Contracts;
using VAL.Host.Abyss;
using VAL.Host.Logging;

namespace VAL.Host.Commands
{
    internal sealed class AbyssCommandHandlers : ICommandRegistryContributor
    {
        private static readonly string[] RequiredIndices = { "indices" };
        private readonly AbyssRuntime _abyssRuntime;
        private readonly RateLimiter _rateLimiter = new();
        private static readonly TimeSpan LogInterval = TimeSpan.FromSeconds(10);
        private static readonly char[] IndexSeparators = { ',', ';', ' ' };

        public AbyssCommandHandlers(AbyssRuntime abyssRuntime)
        {
            _abyssRuntime = abyssRuntime ?? throw new ArgumentNullException(nameof(abyssRuntime));
        }

        public void Register(CommandRegistry registry)
        {
            ArgumentNullException.ThrowIfNull(registry);

            registry.Register(new CommandSpec(
                WebCommandNames.AbyssCommandOpenQueryUi,
                "Abyss",
                Array.Empty<string>(),
                HandleOpenQueryUi));
            registry.Register(new CommandSpec(
                WebCommandNames.AbyssCommandSearch,
                "Abyss",
                Array.Empty<string>(),
                HandleSearch));
            registry.Register(new CommandSpec(
                WebCommandNames.AbyssCommandRetryLast,
                "Abyss",
                Array.Empty<string>(),
                HandleRetryLast));
            registry.Register(new CommandSpec(
                WebCommandNames.AbyssCommandInjectResult,
                "Abyss",
                Array.Empty<string>(),
                HandleInjectResult));
            registry.Register(new CommandSpec(
                WebCommandNames.AbyssCommandInjectResults,
                "Abyss",
                RequiredIndices,
                HandleInjectResults));
            registry.Register(new CommandSpec(
                WebCommandNames.AbyssCommandLast,
                "Abyss",
                Array.Empty<string>(),
                HandleLast));
            registry.Register(new CommandSpec(
                WebCommandNames.AbyssCommandOpenSource,
                "Abyss",
                Array.Empty<string>(),
                HandleOpenSource));
            registry.Register(new CommandSpec(
                WebCommandNames.AbyssCommandClearResults,
                "Abyss",
                Array.Empty<string>(),
                HandleClearResults));
            registry.Register(new CommandSpec(
                WebCommandNames.AbyssCommandDisregard,
                "Abyss",
                Array.Empty<string>(),
                HandleDisregard));
            registry.Register(new CommandSpec(
                WebCommandNames.AbyssCommandGetResults,
                "Abyss",
                Array.Empty<string>(),
                HandleGetResults));
            registry.Register(new CommandSpec(
                WebCommandNames.AbyssCommandInjectPrompt,
                "Abyss",
                Array.Empty<string>(),
                HandleInjectPrompt));
            registry.Register(new CommandSpec(
                WebCommandNames.AbyssCommandInject,
                "Abyss",
                Array.Empty<string>(),
                HandleInject));
        }

        public void HandleSearch(HostCommand cmd)
        {
            try
            {
                cmd.TryGetString("query", out var query);
                cmd.TryGetString("queryOriginal", out var queryOriginal);

                if (string.IsNullOrWhiteSpace(query) && !string.IsNullOrWhiteSpace(queryOriginal))
                    query = queryOriginal;

                var max = 4;
                if (cmd.TryGetInt("maxResults", out var parsed) || cmd.TryGetInt("max", out parsed))
                    max = parsed;

                var exclude = ParseStringList(cmd, "excludeFingerprints");

                _abyssRuntime.Search(cmd.ChatId, query ?? string.Empty, max, queryOriginal, exclude);
            }
            catch (Exception ex)
            {
                LogCommandFailure("search", cmd, ex);
            }
        }

        public void HandleOpenQueryUi(HostCommand cmd)
        {
            // No-op: UI-only command handled client-side.
        }

        public void HandleRetryLast(HostCommand cmd)
        {
            try
            {
                var max = 4;
                if (cmd.TryGetInt("maxResults", out var parsed) || cmd.TryGetInt("max", out parsed))
                    max = parsed;

                var exclude = ParseStringList(cmd, "excludeFingerprints");
                _abyssRuntime.RetryLast(cmd.ChatId, exclude, max);
            }
            catch (Exception ex)
            {
                LogCommandFailure("retry_last", cmd, ex);
            }
        }

        public void HandleInjectResults(HostCommand cmd)
        {
            try
            {
                var indices = ParseIndices(cmd);
                _abyssRuntime.InjectResults(indices, cmd.ChatId);
            }
            catch (Exception ex)
            {
                LogCommandFailure("inject_results", cmd, ex);
            }
        }

        public void HandleInjectResult(HostCommand cmd)
        {
            try
            {
                cmd.TryGetString("id", out var id);
                if (string.IsNullOrWhiteSpace(id))
                    cmd.TryGetString("fingerprint", out id);

                int? index = null;
                if (cmd.TryGetInt("index", out var parsed))
                    index = parsed;

                _abyssRuntime.InjectResult(id, index, cmd.ChatId);
            }
            catch (Exception ex)
            {
                LogCommandFailure("inject_result", cmd, ex);
            }
        }

        public void HandleOpenSource(HostCommand cmd)
        {
            try
            {
                cmd.TryGetString("chatId", out var chatId);
                _abyssRuntime.OpenSource(chatId ?? cmd.ChatId);
            }
            catch (Exception ex)
            {
                LogCommandFailure("open_source", cmd, ex);
            }
        }

        public void HandleGetResults(HostCommand cmd)
        {
            try
            {
                _abyssRuntime.EmitResults(cmd.ChatId);
            }
            catch (Exception ex)
            {
                LogCommandFailure("get_results", cmd, ex);
            }
        }

        public void HandleClearResults(HostCommand cmd)
        {
            try
            {
                _abyssRuntime.ClearResults(cmd.ChatId);
            }
            catch (Exception ex)
            {
                LogCommandFailure("clear_results", cmd, ex);
            }
        }

        public void HandleDisregard(HostCommand cmd)
        {
            // No-op: client maintains snippet-level disregard state.
        }

        public void HandleInjectPrompt(HostCommand cmd)
        {
            try
            {
                _abyssRuntime.InjectPrompt(cmd.ChatId);
            }
            catch (Exception ex)
            {
                LogCommandFailure("inject_prompt", cmd, ex);
            }
        }

        public void HandleInject(HostCommand cmd)
        {
            try
            {
                var indices = ParseIndices(cmd);
                _abyssRuntime.InjectResults(indices, cmd.ChatId);
            }
            catch (Exception ex)
            {
                LogCommandFailure("inject", cmd, ex);
            }
        }

        public void HandleLast(HostCommand cmd)
        {
            try
            {
                var count = 2;
                if (cmd.TryGetInt("count", out var parsed))
                    count = parsed;

                var inject = false;
                if (cmd.TryGetBool("inject", out var parsedBool))
                    inject = parsedBool;

                _abyssRuntime.FetchLast(cmd.ChatId, count, inject);
            }
            catch (Exception ex)
            {
                LogCommandFailure("last", cmd, ex);
            }
        }

        private List<int> ParseIndices(HostCommand cmd)
        {
            var indices = new List<int>();

            try
            {
                if (cmd.Root.TryGetProperty("indices", out var arr) &&
                    arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in arr.EnumerateArray())
                    {
                        if (TryGetInt(el, out var value))
                            indices.Add(value);
                    }
                }
                else if (cmd.Root.TryGetProperty("indices", out var scalar))
                {
                    if (TryGetInt(scalar, out var value))
                        indices.Add(value);
                    else if (scalar.ValueKind == JsonValueKind.String)
                        indices.AddRange(ParseDelimitedIndices(scalar.GetString()));
                }
            }
            catch (Exception ex)
            {
                LogCommandFailure("parse_indices", cmd, ex);
            }

            if (indices.Count == 0 && cmd.TryGetInt("index", out var single))
                indices.Add(single);

            if (indices.Count == 0 && cmd.TryGetString("indices", out var raw))
                indices.AddRange(ParseDelimitedIndices(raw));

            return indices;
        }

        private List<string> ParseStringList(HostCommand cmd, string field)
        {
            var list = new List<string>();

            try
            {
                if (cmd.Root.TryGetProperty(field, out var arr) &&
                    arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in arr.EnumerateArray())
                    {
                        if (el.ValueKind == JsonValueKind.String)
                        {
                            var value = (el.GetString() ?? string.Empty).Trim();
                            if (!string.IsNullOrWhiteSpace(value))
                                list.Add(value);
                        }
                    }
                }
                else if (cmd.Root.TryGetProperty(field, out var scalar) &&
                         scalar.ValueKind == JsonValueKind.String)
                {
                    list.AddRange(ParseDelimitedList(scalar.GetString()));
                }
            }
            catch (Exception ex)
            {
                LogCommandFailure($"parse_list.{field}", cmd, ex);
            }

            if (list.Count == 0 &&
                cmd.Root.TryGetProperty("payload", out var payload) &&
                payload.ValueKind == JsonValueKind.Object)
            {
                try
                {
                    if (payload.TryGetProperty(field, out var nested) &&
                        nested.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var el in nested.EnumerateArray())
                        {
                            if (el.ValueKind == JsonValueKind.String)
                            {
                                var value = (el.GetString() ?? string.Empty).Trim();
                                if (!string.IsNullOrWhiteSpace(value))
                                    list.Add(value);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogCommandFailure($"parse_list.payload.{field}", cmd, ex);
                }
            }

            return list;
        }

        private void LogCommandFailure(string action, HostCommand cmd, Exception ex)
        {
            var key = $"cmd.fail.abyss.{action}";
            if (!_rateLimiter.Allow(key, LogInterval))
                return;

            var sourceHost = cmd.SourceUri?.Host ?? "unknown";
            ValLog.Warn(nameof(AbyssCommandHandlers),
                $"Abyss command failed ({action}) for {cmd.Type} (source: {sourceHost}). {ex.GetType().Name}: {LogSanitizer.Sanitize(ex.Message)}");
        }

        private static IEnumerable<string> ParseDelimitedList(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                yield break;

            var parts = raw.Split(IndexSeparators, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var value = part.Trim();
                if (!string.IsNullOrWhiteSpace(value))
                    yield return value;
            }
        }

        private static bool TryGetInt(JsonElement el, out int value)
        {
            value = 0;
            if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var parsed))
            {
                value = parsed;
                return true;
            }

            if (el.ValueKind == JsonValueKind.String)
            {
                var s = el.GetString();
                if (!string.IsNullOrWhiteSpace(s) && int.TryParse(s, out parsed))
                {
                    value = parsed;
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<int> ParseDelimitedIndices(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                yield break;

            var parts = raw.Split(IndexSeparators, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                if (int.TryParse(part.Trim(), out var parsed) && parsed > 0)
                    yield return parsed;
            }
        }
    }
}
