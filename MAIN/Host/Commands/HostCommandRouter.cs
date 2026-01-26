using System;
using System.Text.Json;
using VAL.Host;
using VAL.Host.WebMessaging;

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
        public static void HandleWebMessage(WebMessageEnvelope message)
        {
            var json = message.Json;
            if (string.IsNullOrWhiteSpace(json))
                return;

            if (!MessageEnvelope.TryParse(json, out var envelope))
                return;

            if (!string.Equals(envelope.Type, "command", StringComparison.OrdinalIgnoreCase))
                return;

            var commandName = envelope.Name?.Trim();
            if (string.IsNullOrWhiteSpace(commandName))
            {
                ValLog.Warn(nameof(HostCommandRouter), "Web message missing command name.");
                return;
            }

            // Central session tracking.
            SessionContext.Observe(commandName, envelope.ChatId);

            var payload = envelope.Payload;
            if (payload.HasValue && payload.Value.ValueKind == JsonValueKind.Object)
            {
                var cmd = new HostCommand(commandName, json, envelope.ChatId, message.SourceUri, payload.Value);
                Dispatch(cmd);
                return;
            }

            using var emptyDoc = JsonDocument.Parse("{}");
            var fallbackCmd = new HostCommand(commandName, json, envelope.ChatId, message.SourceUri, emptyDoc.RootElement);
            Dispatch(fallbackCmd);
        }

        private static void Dispatch(HostCommand cmd)
        {
            if (CommandRegistry.TryDispatch(cmd, out var error))
                return;

            if (error != null)
            {
                ValLog.Warn(nameof(HostCommandRouter), $"Handler error for '{cmd.Type}': {error.GetType().Name}.");
                return;
            }

            ValLog.Warn(nameof(HostCommandRouter), $"Unknown command '{cmd.Type}'.");
        }
    }
}
