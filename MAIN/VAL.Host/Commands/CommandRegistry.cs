using System;
using System.Collections.Generic;

namespace VAL.Host.Commands
{
    public enum CommandDispatchStatus
    {
        Accepted,
        RejectedEmptyType,
        RejectedUnknownType,
        RejectedDeprecated,
        RejectedMissingRequiredField,
        RejectedHandlerException
    }

    public readonly record struct CommandDispatchResult(
        CommandDispatchStatus Status,
        string CommandType,
        string? Module,
        string? Detail,
        Exception? Exception)
    {
        public bool IsAccepted => Status == CommandDispatchStatus.Accepted;
    }

    /// <summary>
    /// Central list of known WebView -> Host commands.
    ///
    /// "Schema-lite" philosophy:
    /// - We only define required fields when it protects correctness.
    /// - Handlers still do their own defensive checks.
    /// - Unknown commands are ignored to keep the host resilient.
    /// </summary>
    public sealed class CommandRegistry
    {
        private readonly Dictionary<string, CommandSpec> _specs =
            new(StringComparer.OrdinalIgnoreCase);

        public void Register(CommandSpec spec)
        {
            if (spec == null || string.IsNullOrWhiteSpace(spec.Type))
                return;

            var key = spec.Type.Trim();
            if (_specs.ContainsKey(key))
            {
                throw new InvalidOperationException($"Command '{key}' is registered more than once.");
            }

            _specs[key] = spec;
        }

        public CommandDispatchResult Dispatch(HostCommand cmd)
        {
            if (string.IsNullOrWhiteSpace(cmd.Type))
            {
                return new CommandDispatchResult(CommandDispatchStatus.RejectedEmptyType, string.Empty, null, "Command name was empty.", null);
            }

            if (!_specs.TryGetValue(cmd.Type, out var spec))
            {
                return new CommandDispatchResult(CommandDispatchStatus.RejectedUnknownType, cmd.Type, null, "Command is not registered.", null);
            }

            if (spec.IsDeprecated)
            {
                var reason = string.IsNullOrWhiteSpace(spec.DeprecationReason)
                    ? "Command has been deprecated and is fenced by the host."
                    : spec.DeprecationReason;

                return new CommandDispatchResult(CommandDispatchStatus.RejectedDeprecated, spec.Type, spec.Module, reason, null);
            }

            if (!ValidateRequiredFields(cmd, spec.RequiredFields, out var missingField))
            {
                return new CommandDispatchResult(
                    CommandDispatchStatus.RejectedMissingRequiredField,
                    spec.Type,
                    spec.Module,
                    $"Missing required field '{missingField}'.",
                    null);
            }

            try
            {
                spec.Handler(cmd);
                return new CommandDispatchResult(CommandDispatchStatus.Accepted, spec.Type, spec.Module, "Dispatched to registered handler.", null);
            }
            catch (Exception ex)
            {
                return new CommandDispatchResult(CommandDispatchStatus.RejectedHandlerException, spec.Type, spec.Module, "Registered handler threw an exception.", ex);
            }
        }

        public bool TryDispatch(HostCommand cmd, out Exception? exception)
        {
            var result = Dispatch(cmd);
            exception = result.Exception;
            return result.IsAccepted;
        }

        private static bool ValidateRequiredFields(HostCommand cmd, string[] required, out string missingField)
        {
            missingField = string.Empty;

            if (required == null || required.Length == 0)
                return true;

            foreach (var field in required)
            {
                if (string.IsNullOrWhiteSpace(field))
                    continue;

                try
                {
                    if (cmd.Root.TryGetProperty(field, out _))
                        continue;

                    if (cmd.Root.TryGetProperty("payload", out var payload) &&
                        payload.ValueKind == System.Text.Json.JsonValueKind.Object &&
                        payload.TryGetProperty(field, out _))
                        continue;

                    missingField = field;
                    return false;
                }
                catch
                {
                    missingField = field;
                    return false;
                }
            }

            return true;
        }
    }
}
