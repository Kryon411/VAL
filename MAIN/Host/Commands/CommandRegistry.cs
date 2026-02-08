using System;
using System.Collections.Generic;
using VAL.Continuum;
using VAL.Contracts;
using VAL.Host.Logging;

namespace VAL.Host.Commands
{
    /// <summary>
    /// Central list of known WebView -> Host commands.
    ///
    /// "Schema-lite" philosophy:
    /// - We only define required fields when it protects correctness.
    /// - Handlers still do their own defensive checks.
    /// - Unknown commands are ignored to keep the host resilient.
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
                WebCommandNames.VoidCommandSetEnabled,
                "Void",
                new[] { "enabled" },
                VoidCommandHandlers.HandleSetEnabled
            ));

            // ---- Continuum ----
            // Explicit list so the host has a single discoverable map of supported commands.
            var continuumTypes = new[]
            {
                WebCommandNames.ContinuumCaptureFlushAck,
                WebCommandNames.ContinuumSessionAttach,
                WebCommandNames.ContinuumSessionAttached,
                WebCommandNames.ContinuumCommandToggleLogging,

                WebCommandNames.ContinuumUiNewChat,
                WebCommandNames.ContinuumUiPreludePrompt,
                WebCommandNames.ContinuumUiComposerInteraction,

                WebCommandNames.ContinuumCommandInjectPreamble,
                WebCommandNames.ContinuumCommandInjectPrelude,

                WebCommandNames.ContinuumTruthAppend,
                WebCommandNames.TruthAppend,
                WebCommandNames.ContinuumTruth,

                WebCommandNames.ContinuumCommandPulse,
                WebCommandNames.ContinuumCommandRefreshQuick,

                WebCommandNames.ContinuumCommandOpenSessionFolder,

                WebCommandNames.ContinuumCommandChronicleCancel,
                WebCommandNames.ContinuumCommandCancelChronicle,
                WebCommandNames.ContinuumCommandChronicleRebuildTruth,
                WebCommandNames.ContinuumCommandChronicle,
                WebCommandNames.ContinuumChronicleProgress,
                WebCommandNames.ContinuumChronicleDone,

                WebCommandNames.InjectSuccess,
                WebCommandNames.ContinuumEvent,
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
                WebCommandNames.AbyssCommandOpenQueryUi,
                "Abyss",
                Array.Empty<string>(),
                AbyssCommandHandlers.HandleOpenQueryUi
            ));

            Register(new CommandSpec(
                WebCommandNames.AbyssCommandSearch,
                "Abyss",
                Array.Empty<string>(),
                AbyssCommandHandlers.HandleSearch
            ));

            Register(new CommandSpec(
                WebCommandNames.AbyssCommandRetryLast,
                "Abyss",
                Array.Empty<string>(),
                AbyssCommandHandlers.HandleRetryLast
            ));

            Register(new CommandSpec(
                WebCommandNames.AbyssCommandInjectResult,
                "Abyss",
                Array.Empty<string>(),
                AbyssCommandHandlers.HandleInjectResult
            ));

            Register(new CommandSpec(
                WebCommandNames.AbyssCommandInjectResults,
                "Abyss",
                new[] { "indices" },
                AbyssCommandHandlers.HandleInjectResults
            ));

            Register(new CommandSpec(
                WebCommandNames.AbyssCommandLast,
                "Abyss",
                Array.Empty<string>(),
                AbyssCommandHandlers.HandleLast
            ));

            Register(new CommandSpec(
                WebCommandNames.AbyssCommandOpenSource,
                "Abyss",
                Array.Empty<string>(),
                AbyssCommandHandlers.HandleOpenSource
            ));

            Register(new CommandSpec(
                WebCommandNames.AbyssCommandClearResults,
                "Abyss",
                Array.Empty<string>(),
                AbyssCommandHandlers.HandleClearResults
            ));

            Register(new CommandSpec(
                WebCommandNames.AbyssCommandDisregard,
                "Abyss",
                Array.Empty<string>(),
                AbyssCommandHandlers.HandleDisregard
            ));

            Register(new CommandSpec(
                WebCommandNames.AbyssCommandGetResults,
                "Abyss",
                Array.Empty<string>(),
                AbyssCommandHandlers.HandleGetResults
            ));

            Register(new CommandSpec(
                WebCommandNames.AbyssCommandInjectPrompt,
                "Abyss",
                Array.Empty<string>(),
                AbyssCommandHandlers.HandleInjectPrompt
            ));

            Register(new CommandSpec(
                WebCommandNames.AbyssCommandInject,
                "Abyss",
                Array.Empty<string>(),
                AbyssCommandHandlers.HandleInject
            ));

            // ---- Portal ----
            Register(new CommandSpec(
                WebCommandNames.PortalCommandSetEnabled,
                "Portal",
                new[] { "enabled" },
                PortalCommandHandlers.HandleSetEnabled
            ));

            // Accept both command names (old/new) and map to same handler.
            Register(new CommandSpec(
                WebCommandNames.PortalCommandOpenSnip,
                "Portal",
                Array.Empty<string>(),
                PortalCommandHandlers.HandleOpenSnip
            ));

            Register(new CommandSpec(
                WebCommandNames.PortalCommandOpenSnipOverlay,
                "Portal",
                Array.Empty<string>(),
                PortalCommandHandlers.HandleOpenSnip
            ));

            Register(new CommandSpec(
                WebCommandNames.PortalCommandSendStaged,
                "Portal",
                Array.Empty<string>(),
                PortalCommandHandlers.HandleSendStaged
            ));

            Register(new CommandSpec(
                WebCommandNames.PortalCommandSend,
                "Portal",
                Array.Empty<string>(),
                PortalCommandHandlers.HandleSendStaged
            ));

            Register(new CommandSpec(
                WebCommandNames.PortalCommandSendStagedLegacy,
                "Portal",
                Array.Empty<string>(),
                PortalCommandHandlers.HandleSendStaged
            ));

            // ---- Privacy ----
            Register(new CommandSpec(
                WebCommandNames.PrivacyCommandSetContinuumLogging,
                "Privacy",
                new[] { "enabled" },
                PrivacyCommandHandlers.HandleSetContinuumLogging
            ));

            Register(new CommandSpec(
                WebCommandNames.PrivacyCommandSetPortalCapture,
                "Privacy",
                new[] { "enabled" },
                PrivacyCommandHandlers.HandleSetPortalCapture
            ));

            Register(new CommandSpec(
                WebCommandNames.PrivacyCommandOpenDataFolder,
                "Privacy",
                Array.Empty<string>(),
                PrivacyCommandHandlers.HandleOpenDataFolder
            ));

            Register(new CommandSpec(
                WebCommandNames.PrivacyCommandWipeData,
                "Privacy",
                Array.Empty<string>(),
                PrivacyCommandHandlers.HandleWipeData
            ));

            // ---- Tools ----
            Register(new CommandSpec(
                WebCommandNames.ToolsOpenTruthHealth,
                "Tools",
                Array.Empty<string>(),
                ToolsCommandHandlers.HandleOpenTruthHealth
            ));

            Register(new CommandSpec(
                WebCommandNames.ToolsOpenDiagnostics,
                "Tools",
                Array.Empty<string>(),
                ToolsCommandHandlers.HandleOpenDiagnostics
            ));

            // ---- Dock ----
            Register(new CommandSpec(
                WebCommandNames.DockCommandRequestModel,
                "Dock",
                Array.Empty<string>(),
                DockCommandHandlers.HandleRequestModel
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
