using System;
using System.Text.Json;
using System.Threading;
using VAL.Host;
using VAL.Host.Security;
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
        private static readonly long BlockedTypeLogIntervalTicks = TimeSpan.FromSeconds(10).Ticks;
        private static long _lastBlockedTypeLogTicks;

        public static void HandleWebMessage(WebMessageEnvelope webMessage)
        {
            var json = webMessage.Json;
            if (string.IsNullOrWhiteSpace(json))
                return;

            if (!WebMessageType.TryGetType(json, out var messageType))
                return;

            if (!WebCommandRegistry.IsAllowed(messageType))
            {
                LogBlockedType(messageType, webMessage.SourceUri);
                return;
            }

            if (!MessageEnvelope.TryParse(json, out var parsedEnvelope))
                return;

            if (!string.Equals(parsedEnvelope.Type, "command", StringComparison.OrdinalIgnoreCase))
                return;

            var commandName = parsedEnvelope.Name?.Trim();
            if (string.IsNullOrWhiteSpace(commandName))
            {
                ValLog.Warn(nameof(HostCommandRouter), "Web message missing command name.");
                return;
            }

            if (!WebCommandRegistry.IsAllowed(commandName))
            {
                LogBlockedType(commandName, webMessage.SourceUri);
                return;
            }

            // Central session tracking.
            SessionContext.Observe(commandName, parsedEnvelope.ChatId);

            var payload = parsedEnvelope.Payload;
            if (payload.HasValue && payload.Value.ValueKind == JsonValueKind.Object)
            {
                var cmd = new HostCommand(commandName, json, parsedEnvelope.ChatId, webMessage.SourceUri, payload.Value);
                Dispatch(cmd);
                return;
            }

            using var emptyDoc = JsonDocument.Parse("{}");
            var fallbackCmd = new HostCommand(commandName, json, parsedEnvelope.ChatId, webMessage.SourceUri, emptyDoc.RootElement);
            Dispatch(fallbackCmd);
        }

        private static void Dispatch(HostCommand cmd)
        {
            if (CommandRegistry.TryDispatch(cmd, out var error))
                return;

            if (error != null)
            {
                if (IsDiagnosticsCommand(cmd))
                {
                    ToolsCommandHandlers.ReportDiagnosticsFailure(cmd, error, "exception");
                    return;
                }

                ValLog.Warn(nameof(HostCommandRouter), $"Handler error for '{cmd.Type}': {error.GetType().Name}.");
                return;
            }

            if (IsDiagnosticsCommand(cmd))
            {
                ToolsCommandHandlers.ReportDiagnosticsFailure(cmd, null, "unhandled");
                return;
            }

            ValLog.Warn(nameof(HostCommandRouter), $"Unknown command '{cmd.Type}'.");
        }

        private static bool IsDiagnosticsCommand(HostCommand cmd)
        {
            return string.Equals(cmd.Type, "tools.open_diagnostics", StringComparison.Ordinal);
        }

        private static void LogBlockedType(string type, Uri? sourceUri)
        {
            var nowTicks = DateTimeOffset.UtcNow.Ticks;
            var lastTicks = Interlocked.Read(ref _lastBlockedTypeLogTicks);
            if (nowTicks - lastTicks < BlockedTypeLogIntervalTicks)
                return;

            if (Interlocked.CompareExchange(ref _lastBlockedTypeLogTicks, nowTicks, lastTicks) != lastTicks)
                return;

            var sourceHost = sourceUri?.Host ?? "<unknown>";
            ValLog.Warn(nameof(HostCommandRouter), $"Blocked unknown web message type: {type} (source: {sourceHost})");
        }
    }
}
