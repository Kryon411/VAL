using System;
using System.Text.Json;
using System.Threading;
using VAL.Contracts;
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
    public sealed class HostCommandRouter
    {
        private static readonly long BlockedTypeLogIntervalTicks = TimeSpan.FromSeconds(10).Ticks;
        private static long _lastBlockedTypeLogTicks;

        private readonly CommandRegistry _commandRegistry;
        private readonly ICommandDiagnosticsReporter? _diagnosticsReporter;

        public HostCommandRouter(CommandRegistry commandRegistry, ICommandDiagnosticsReporter? diagnosticsReporter = null)
        {
            _commandRegistry = commandRegistry ?? throw new ArgumentNullException(nameof(commandRegistry));
            _diagnosticsReporter = diagnosticsReporter;
        }

        public void HandleWebMessage(WebMessageEnvelope webMessage)
        {
            var json = webMessage.Json;
            if (string.IsNullOrWhiteSpace(json))
                return;

            if (!WebMessageType.TryGetType(json, out var messageType))
                return;

            if (!WebCommandRegistry.IsAllowed(messageType))
            {
                LogBlockedType(messageType, webMessage.SourceUri, "message-type-allowlist");
                return;
            }

            if (!MessageEnvelope.TryParse(json, out var parsedEnvelope))
                return;

            if (!string.Equals(parsedEnvelope.Type, WebMessageTypes.Command, StringComparison.OrdinalIgnoreCase))
                return;

            var commandName = parsedEnvelope.Name?.Trim();
            if (string.IsNullOrWhiteSpace(commandName))
            {
                ValLog.Warn(nameof(HostCommandRouter), "Web message missing command name.");
                return;
            }

            if (!WebCommandRegistry.IsAllowed(commandName))
            {
                LogBlockedType(commandName, webMessage.SourceUri, "command-allowlist");
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

        private void Dispatch(HostCommand cmd)
        {
            var result = _commandRegistry.Dispatch(cmd);
            if (result.IsAccepted)
                return;

            if (IsDiagnosticsCommand(cmd))
            {
                _diagnosticsReporter?.ReportDiagnosticsFailure(cmd, result.Exception, result.Status.ToString());
                return;
            }

            switch (result.Status)
            {
                case CommandDispatchStatus.RejectedHandlerException:
                    ValLog.Warn(nameof(HostCommandRouter),
                        $"Command rejected '{cmd.Type}' (module: {result.Module ?? "<unknown>"}, reason: handler-exception, detail: {result.Detail}, exception: {result.Exception?.GetType().Name}).");
                    return;

                case CommandDispatchStatus.RejectedMissingRequiredField:
                case CommandDispatchStatus.RejectedUnknownType:
                case CommandDispatchStatus.RejectedDeprecated:
                case CommandDispatchStatus.RejectedEmptyType:
                    ValLog.Warn(nameof(HostCommandRouter),
                        $"Command rejected '{cmd.Type}' (module: {result.Module ?? "<unknown>"}, reason: {result.Status}, detail: {result.Detail}).");
                    return;

                default:
                    ValLog.Warn(nameof(HostCommandRouter),
                        $"Command rejected '{cmd.Type}' (module: {result.Module ?? "<unknown>"}, reason: unknown-rejection).");
                    return;
            }
        }

        private static bool IsDiagnosticsCommand(HostCommand cmd)
        {
            return string.Equals(cmd.Type, WebCommandNames.ToolsOpenDiagnostics, StringComparison.Ordinal);
        }

        private static void LogBlockedType(string type, Uri? sourceUri, string reason)
        {
            var nowTicks = DateTimeOffset.UtcNow.Ticks;
            var lastTicks = Interlocked.Read(ref _lastBlockedTypeLogTicks);
            if (nowTicks - lastTicks < BlockedTypeLogIntervalTicks)
                return;

            if (Interlocked.CompareExchange(ref _lastBlockedTypeLogTicks, nowTicks, lastTicks) != lastTicks)
                return;

            var sourceHost = sourceUri?.Host ?? "<unknown>";
            ValLog.Warn(nameof(HostCommandRouter), $"Blocked web message type: {type} (source: {sourceHost}, reason: {reason})");
        }
    }
}
