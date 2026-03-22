using System;
using System.Text.Json;
using System.Threading;
using VAL.Contracts;
using VAL.Host.Security;
using VAL.Host.Services;
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
        private static readonly JsonElement EmptyPayload = JsonSerializer.SerializeToElement(new { });

        private readonly CommandRegistry _commandRegistry;
        private readonly ICommandDiagnosticsReporter? _diagnosticsReporter;
        private readonly ISessionContext _sessionContext;

        public HostCommandRouter(
            CommandRegistry commandRegistry,
            ICommandDiagnosticsReporter? diagnosticsReporter = null,
            ISessionContext? sessionContext = null)
        {
            _commandRegistry = commandRegistry ?? throw new ArgumentNullException(nameof(commandRegistry));
            _diagnosticsReporter = diagnosticsReporter;
            _sessionContext = sessionContext ?? new SessionContext();
        }

        public HostCommandExecutionResult HandleWebMessage(WebMessageEnvelope webMessage)
        {
            var json = webMessage.Json;
            if (string.IsNullOrWhiteSpace(json))
                return HostCommandExecutionResult.Blocked("<empty>", "Command payload was empty.", isDockInvocation: false, diagnosticDetail: "empty-json");

            if (!webMessage.TryGetParsedEnvelope(out var parsedEnvelope))
                return HostCommandExecutionResult.Blocked("<unknown>", "Command payload was invalid.", isDockInvocation: false, diagnosticDetail: "invalid-message-type");

            var messageType = parsedEnvelope.Type?.Trim();
            if (string.IsNullOrWhiteSpace(messageType))
                return HostCommandExecutionResult.Blocked("<unknown>", "Command payload was invalid.", isDockInvocation: false, diagnosticDetail: "missing-message-type");

            if (!WebCommandRegistry.IsAllowed(messageType))
            {
                LogBlockedType(messageType, webMessage.SourceUri, "message-type-allowlist");
                return HostCommandExecutionResult.Blocked(messageType, "Command is not allowed by host policy.", isDockInvocation: false, diagnosticDetail: "message-type-allowlist");
            }

            var isDockInvocation = IsDockMessageSource(parsedEnvelope.Source);

            if (!string.Equals(parsedEnvelope.Type, WebMessageTypes.Command, StringComparison.OrdinalIgnoreCase))
                return HostCommandExecutionResult.Blocked(parsedEnvelope.Type ?? "<unknown>", "Message type is not dispatchable.", isDockInvocation, diagnosticDetail: "non-command-envelope");

            var commandName = parsedEnvelope.Name?.Trim();
            if (string.IsNullOrWhiteSpace(commandName))
            {
                ValLog.Warn(nameof(HostCommandRouter), "Web message missing command name.");
                return HostCommandExecutionResult.Blocked("<missing>", "Command name was missing.", isDockInvocation, diagnosticDetail: "missing-command-name");
            }

            if (!WebCommandRegistry.IsAllowed(commandName))
            {
                LogBlockedType(commandName, webMessage.SourceUri, "command-allowlist");
                return HostCommandExecutionResult.Blocked(commandName, "Command is not allowed by host policy.", isDockInvocation, diagnosticDetail: "command-allowlist");
            }

            // Central session tracking.
            _sessionContext.Observe(commandName, parsedEnvelope.ChatId);

            var payload = parsedEnvelope.Payload;
            var payloadRoot = payload.HasValue && payload.Value.ValueKind == JsonValueKind.Object
                ? payload.Value
                : EmptyPayload;

            var cmd = new HostCommand(commandName, json, parsedEnvelope.ChatId, webMessage.SourceUri, payloadRoot);
            return Dispatch(cmd, isDockInvocation);
        }

        private HostCommandExecutionResult Dispatch(HostCommand cmd, bool isDockInvocation)
        {
            var result = _commandRegistry.Dispatch(cmd);
            if (result.IsAccepted)
                return HostCommandExecutionResult.Success(cmd.Type, isDockInvocation);

            if (IsDiagnosticsCommand(cmd))
            {
                _diagnosticsReporter?.ReportDiagnosticsFailure(cmd, result.Exception, result.Status.ToString());
                return HostCommandExecutionResult.Error(cmd.Type, "Diagnostics command failed.", isDockInvocation, result.Exception, result.Status.ToString());
            }

            switch (result.Status)
            {
                case CommandDispatchStatus.RejectedHandlerException:
                    ValLog.Warn(nameof(HostCommandRouter),
                        $"Command rejected '{cmd.Type}' (module: {result.Module ?? "<unknown>"}, reason: handler-exception, detail: {result.Detail}, exception: {result.Exception?.GetType().Name}).");
                    return HostCommandExecutionResult.Error(cmd.Type, "Command failed while executing.", isDockInvocation, result.Exception, result.Detail);

                case CommandDispatchStatus.RejectedMissingRequiredField:
                case CommandDispatchStatus.RejectedUnknownType:
                case CommandDispatchStatus.RejectedDeprecated:
                case CommandDispatchStatus.RejectedEmptyType:
                    ValLog.Warn(nameof(HostCommandRouter),
                        $"Command rejected '{cmd.Type}' (module: {result.Module ?? "<unknown>"}, reason: {result.Status}, detail: {result.Detail}).");
                    return HostCommandExecutionResult.Blocked(cmd.Type, "Command was blocked by host validation.", isDockInvocation, result.Detail);

                default:
                    ValLog.Warn(nameof(HostCommandRouter),
                        $"Command rejected '{cmd.Type}' (module: {result.Module ?? "<unknown>"}, reason: unknown-rejection).");
                    return HostCommandExecutionResult.Error(cmd.Type, "Command failed with an unknown host error.", isDockInvocation, result.Exception, result.Detail);
            }
        }

        private static bool IsDockMessageSource(string? source)
        {
            return string.Equals(source?.Trim(), "dock", StringComparison.OrdinalIgnoreCase);
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
