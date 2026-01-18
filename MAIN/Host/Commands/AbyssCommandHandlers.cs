using System;
using VAL.Host.Abyss;

namespace VAL.Host.Commands
{
    internal static class AbyssCommandHandlers
    {
        public static void HandleSearch(HostCommand cmd)
        {
            try
            {
                string? queryOriginal = null;
                if (cmd.TryGetString("queryOriginal", out var q)) queryOriginal = q;

                string[]? exclude = null;
                if (cmd.Root.TryGetProperty("excludeChatIds", out var ex) && ex.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    var list = new System.Collections.Generic.List<string>();
                    foreach (var item in ex.EnumerateArray())
                    {
                        if (item.ValueKind == System.Text.Json.JsonValueKind.String)
                        {
                            var v = item.GetString();
                            if (!string.IsNullOrWhiteSpace(v)) list.Add(v.Trim());
                        }
                    }
                    exclude = list.Count > 0 ? list.ToArray() : null;
                }

                int? maxResults = null;
                if (cmd.TryGetInt("maxResults", out var max)) maxResults = max;

                AbyssRuntime.Search(cmd.ChatId, queryOriginal, exclude, maxResults);
            }
            catch { }
        }

        public static void HandleInjectResults(HostCommand cmd)
        {
            try
            {
                if (!cmd.TryGetString("indices", out var indices))
                    return;

                AbyssRuntime.InjectResults(cmd.ChatId, indices);
            }
            catch { }
        }

        public static void HandleLast(HostCommand cmd)
        {
            try { AbyssRuntime.InjectLast(cmd.ChatId); } catch { }
        }

        public static void HandleOpenSource(HostCommand cmd)
        {
            try
            {
                if (!cmd.TryGetString("chatId", out var chatId))
                    chatId = cmd.ChatId;

                var truthPath = cmd.TryGetString("truthPath", out var tp) ? tp : null;
                AbyssRuntime.OpenSource(chatId, truthPath);
            }
            catch { }
        }

        public static void HandleGetResults(HostCommand cmd)
        {
            try { AbyssRuntime.SendResults(cmd.ChatId); } catch { }
        }
    }
}
