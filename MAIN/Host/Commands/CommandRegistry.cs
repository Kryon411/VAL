using System;
using System.Collections.Generic;
using VAL.Continuum;
using VAL.Continuum.Pipeline.QuickRefresh;
using VAL.Host.Logging;

namespace VAL.Host.Commands
{
    /// <summary>
    /// Central list of known WebView -> Host commands.
    ///
    /// "Schema-lite" philosophy:
    /// - We only define required fields when it protects correctness.
    /// - Handlers still do their own defensive checks.
    /// - Unknown commands are ignored (or routed by prefix) to keep the host resilient.
    /// </summary>
    internal static class CommandRegistry
    {
        private static readonly Dictionary<string, CommandSpec> Specs =
            new Dictionary<string, CommandSpec>(StringComparer.OrdinalIgnoreCase);
        private static readonly RateLimiter RateLimiter = new();
        private static readonly TimeSpan LogInterval = TimeSpan.FromSeconds(10);

        static CommandRegistry()
        {
            // ---- Void ----
            Register(new CommandSpec(
                "void.command.set_enabled",
                "Void",
                new[] { "enabled" },
                VoidCommandHandlers.HandleSetEnabled
            ));

            // ---- Continuum ----
            // Explicit list so the host has a single discoverable map of supported commands.
            // (We still have a prefix fallback for forward compatibility.)
            var continuumTypes = new[]
            {
                "continuum.capture.flush_ack",
                "continuum.session.attach",
                "continuum.session.attached",
                "continuum.command.toggle_logging",

                "continuum.ui.new_chat",
                "continuum.ui.prelude_prompt",
                "continuum.ui.composer_interaction",

                "continuum.command.inject_preamble",
                "continuum.command.inject_prelude",

                "continuum.truth.append",
                "truth.append",
                "continuum.truth",

                QuickRefreshCommands.CommandPulse,
                QuickRefreshCommands.CommandRefreshQuick,

                "continuum.command.open_session_folder",

                "continuum.command.chronicle_cancel",
                "continuum.command.cancel_chronicle",
                "continuum.command.chronicle_rebuild_truth",
                "continuum.command.chronicle",
                "continuum.chronicle.progress",
                "continuum.chronicle.done",

                "inject.success",
                "continuum.event",
            };

            foreach (var t in continuumTypes)
            {
                Register(new CommandSpec(
                    t,
                    "Continuum",
                    Array.Empty<string>(),
                    HandleContinuumCommand
                ));
            }

            // ---- Abyss ----
            Register(new CommandSpec(
                "abyss.command.open_query_ui",
                "Abyss",
                Array.Empty<string>(),
                AbyssCommandHandlers.HandleOpenQueryUi
            ));

            Register(new CommandSpec(
                "abyss.command.search",
                "Abyss",
                Array.Empty<string>(),
                AbyssCommandHandlers.HandleSearch
            ));

            Register(new CommandSpec(
                "abyss.command.retry_last",
                "Abyss",
                Array.Empty<string>(),
                AbyssCommandHandlers.HandleRetryLast
            ));

            Register(new CommandSpec(
                "abyss.command.inject_result",
                "Abyss",
                Array.Empty<string>(),
                AbyssCommandHandlers.HandleInjectResult
            ));

            Register(new CommandSpec(
                "abyss.command.inject_results",
                "Abyss",
                new[] { "indices" },
                AbyssCommandHandlers.HandleInjectResults
            ));

            Register(new CommandSpec(
                "abyss.command.last",
                "Abyss",
                Array.Empty<string>(),
                AbyssCommandHandlers.HandleLast
            ));

            Register(new CommandSpec(
                "abyss.command.open_source",
                "Abyss",
                Array.Empty<string>(),
                AbyssCommandHandlers.HandleOpenSource
            ));

            Register(new CommandSpec(
                "abyss.command.clear_results",
                "Abyss",
                Array.Empty<string>(),
                AbyssCommandHandlers.HandleClearResults
            ));

            Register(new CommandSpec(
                "abyss.command.disregard",
                "Abyss",
                Array.Empty<string>(),
                AbyssCommandHandlers.HandleDisregard
            ));

            Register(new CommandSpec(
                "abyss.command.get_results",
                "Abyss",
                Array.Empty<string>(),
                AbyssCommandHandlers.HandleGetResults
            ));

            Register(new CommandSpec(
                "abyss.command.inject_prompt",
                "Abyss",
                Array.Empty<string>(),
                AbyssCommandHandlers.HandleInjectPrompt
            ));

            Register(new CommandSpec(
                "abyss.command.inject",
                "Abyss",
                Array.Empty<string>(),
                AbyssCommandHandlers.HandleInject
            ));

            // ---- Portal ----
            Register(new CommandSpec(
                "portal.command.set_enabled",
                "Portal",
                new[] { "enabled" },
                PortalCommandHandlers.HandleSetEnabled
            ));

            // Accept both command names (old/new) and map to same handler.
            Register(new CommandSpec(
                "portal.command.open_snip",
                "Portal",
                Array.Empty<string>(),
                PortalCommandHandlers.HandleOpenSnip
            ));

            Register(new CommandSpec(
                "portal.command.open_snip_overlay",
                "Portal",
                Array.Empty<string>(),
                PortalCommandHandlers.HandleOpenSnip
            ));

            Register(new CommandSpec(
                "portal.command.send_staged",
                "Portal",
                Array.Empty<string>(),
                PortalCommandHandlers.HandleSendStaged
            ));

            Register(new CommandSpec(
                "portal.command.send",
                "Portal",
                Array.Empty<string>(),
                PortalCommandHandlers.HandleSendStaged
            ));

            Register(new CommandSpec(
                "portal.command.sendStaged",
                "Portal",
                Array.Empty<string>(),
                PortalCommandHandlers.HandleSendStaged
            ));

            // ---- Tools ----
            Register(new CommandSpec(
                "tools.open_truth_health",
                "Tools",
                Array.Empty<string>(),
                ToolsCommandHandlers.HandleOpenTruthHealth
            ));

            Register(new CommandSpec(
                "tools.open_diagnostics",
                "Tools",
                Array.Empty<string>(),
                ToolsCommandHandlers.HandleOpenDiagnostics
            ));
        }

        private static void HandleContinuumCommand(HostCommand cmd)
        {
            try
            {
                ContinuumHost.HandleJson(cmd.RawJson);
            }
            catch (Exception ex)
            {
                LogHandlerFailure("cmd.fail.continuum", cmd, ex);
            }
        }

        private static void LogHandlerFailure(string key, HostCommand cmd, Exception ex)
        {
            if (!RateLimiter.Allow(key, LogInterval))
                return;

            var sourceHost = cmd.SourceUri?.Host ?? "unknown";
            ValLog.Warn(nameof(CommandRegistry),
                $"Command handler failed for {cmd.Type} (source: {sourceHost}). {ex.GetType().Name}: {LogSanitizer.Sanitize(ex.Message)}");
        }

        private static void Register(CommandSpec spec)
        {
            if (spec == null || string.IsNullOrWhiteSpace(spec.Type))
                return;

            Specs[spec.Type.Trim()] = spec;
        }

        public static bool TryDispatch(HostCommand cmd, out Exception? exception)
        {
            exception = null;

            if (string.IsNullOrWhiteSpace(cmd.Type))
                return false;

            // 1) Exact match.
            if (Specs.TryGetValue(cmd.Type, out var spec))
            {
                if (!ValidateRequiredFields(cmd, spec.RequiredFields))
                    return false;

                try
                {
                    spec.Handler(cmd);
                    return true;
                }
                catch (Exception ex)
                {
                    exception = ex;
                    return false;
                }
            }

            // 2) Forward-compat routing: any Continuum command we haven't catalogued yet.
            //    ContinuumHost remains defensive and will ignore unknown types safely.
            if (cmd.Type.StartsWith("continuum.", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    ContinuumHost.HandleJson(cmd.RawJson);
                    return true;
                }
                catch (Exception ex)
                {
                    exception = ex;
                    return false;
                }
            }

            return false;
        }

        private static bool ValidateRequiredFields(HostCommand cmd, string[] required)
        {
            if (required == null || required.Length == 0)
                return true;

            foreach (var field in required)
            {
                if (string.IsNullOrWhiteSpace(field)) continue;

                // If it's required, it must exist (any value kind). Use TryGetString/bool only when needed.
                try
                {
                    // Prefer root-level fields...
                    if (cmd.Root.TryGetProperty(field, out _))
                        continue;

                    // ...but allow required fields under a "payload" object too.
                    if (cmd.Root.TryGetProperty("payload", out var payload) &&
                        payload.ValueKind == System.Text.Json.JsonValueKind.Object &&
                        payload.TryGetProperty(field, out _))
                        continue;

                    return false;
                }
                catch
                {
                    return false;
                }
            }

            return true;
        }
    }
}
