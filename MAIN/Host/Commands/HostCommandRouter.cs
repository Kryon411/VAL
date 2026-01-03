using System;
using System.Text.Json;

namespace VAL.Host.Commands
{
    /// <summary>
    /// Single entry-point for all WebView -> Host messages.
    ///
    /// Responsibilities:
    /// 1) Parse the message envelope (type + optional chatId)
    /// 2) Update SessionContext (authoritative chat tracking)
    /// 3) Dispatch via CommandRegistry
    ///
    /// This keeps MainWindow free of command-specific logic.
    /// </summary>
    public static class HostCommandRouter
    {
        public static void Handle(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("type", out var typeEl) || typeEl.ValueKind != JsonValueKind.String)
                    return;

                var type = (typeEl.GetString() ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(type))
                    return;

                string? chatId = null;
                try
                {
                    if (root.TryGetProperty("chatId", out var chatEl) && chatEl.ValueKind == JsonValueKind.String)
                        chatId = chatEl.GetString();
                }
                catch { /* ignore */ }

                // Central session tracking.
                SessionContext.Observe(type, chatId);

                var cmd = new HostCommand(type, json, chatId, root);
                CommandRegistry.TryDispatch(cmd);
            }
            catch
            {
                // Never let malformed JSON break the host.
            }
        }
    }
}
