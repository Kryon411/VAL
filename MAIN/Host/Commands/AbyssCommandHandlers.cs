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

                var max = 8;
                if (cmd.TryGetInt("max", out var parsed))
                    max = parsed;

                AbyssRuntime.Search(cmd.ChatId, query ?? string.Empty, max, queryOriginal);
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

        public static void HandleOpenSource(HostCommand cmd)
        {
            try
            {
                cmd.TryGetString("truthPath", out var truthPath);
                AbyssRuntime.OpenSource(truthPath, cmd.ChatId);
            }
            catch { }
        }

        public static void HandleGetResults(HostCommand cmd)
        {
            try { AbyssRuntime.EmitResults(cmd.ChatId); } catch { }
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
