using System;
using System.Collections.Generic;
using System.Text.Json;
using VAL.Host.Abyss;

namespace VAL.Host.Commands
{
    internal static class AbyssCommandHandlers
    {
        public static void HandleSearch(HostCommand cmd)
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

                AbyssRuntime.Search(cmd.ChatId, query ?? string.Empty, max, queryOriginal, exclude);
            }
            catch { }
        }

        public static void HandleOpenQueryUi(HostCommand cmd)
        {
            // No-op: UI-only command handled client-side.
        }

        public static void HandleRetryLast(HostCommand cmd)
        {
            try
            {
                var max = 4;
                if (cmd.TryGetInt("maxResults", out var parsed) || cmd.TryGetInt("max", out parsed))
                    max = parsed;

                var exclude = ParseStringList(cmd, "excludeFingerprints");
                AbyssRuntime.RetryLast(cmd.ChatId, exclude, max);
            }
            catch { }
        }

        public static void HandleInjectResults(HostCommand cmd)
        {
            try
            {
                var indices = ParseIndices(cmd);
                AbyssRuntime.InjectResults(indices, cmd.ChatId);
            }
            catch { }
        }

        public static void HandleInjectResult(HostCommand cmd)
        {
            try
            {
                cmd.TryGetString("id", out var id);
                if (string.IsNullOrWhiteSpace(id))
                    cmd.TryGetString("fingerprint", out id);

                int? index = null;
                if (cmd.TryGetInt("index", out var parsed))
                    index = parsed;

                AbyssRuntime.InjectResult(id, index, cmd.ChatId);
            }
            catch { }
        }

        public static void HandleOpenSource(HostCommand cmd)
        {
            try
            {
                cmd.TryGetString("chatId", out var chatId);
                AbyssRuntime.OpenSource(chatId ?? cmd.ChatId);
            }
            catch { }
        }

        public static void HandleGetResults(HostCommand cmd)
        {
            try { AbyssRuntime.EmitResults(cmd.ChatId); } catch { }
        }

        public static void HandleClearResults(HostCommand cmd)
        {
            try { AbyssRuntime.ClearResults(cmd.ChatId); } catch { }
        }

        public static void HandleDisregard(HostCommand cmd)
        {
            // No-op: client maintains snippet-level disregard state.
        }

        public static void HandleInjectPrompt(HostCommand cmd)
        {
            try { AbyssRuntime.InjectPrompt(cmd.ChatId); } catch { }
        }

        public static void HandleInject(HostCommand cmd)
        {
            try
            {
                var indices = ParseIndices(cmd);
                AbyssRuntime.InjectResults(indices, cmd.ChatId);
            }
            catch { }
        }

        public static void HandleLast(HostCommand cmd)
        {
            try
            {
                var count = 2;
                if (cmd.TryGetInt("count", out var parsed))
                    count = parsed;

                var inject = false;
                if (cmd.TryGetBool("inject", out var parsedBool))
                    inject = parsedBool;

                AbyssRuntime.FetchLast(cmd.ChatId, count, inject);
            }
            catch { }
        }

        private static List<int> ParseIndices(HostCommand cmd)
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
            catch { }

            if (indices.Count == 0 && cmd.TryGetInt("index", out var single))
                indices.Add(single);

            if (indices.Count == 0 && cmd.TryGetString("indices", out var raw))
                indices.AddRange(ParseDelimitedIndices(raw));

            return indices;
        }

        private static List<string> ParseStringList(HostCommand cmd, string field)
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
            catch { }

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
                catch { }
            }

            return list;
        }

        private static IEnumerable<string> ParseDelimitedList(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                yield break;

            var parts = raw.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
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

            var parts = raw.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                if (int.TryParse(part.Trim(), out var parsed) && parsed > 0)
                    yield return parsed;
            }
        }
    }
}
