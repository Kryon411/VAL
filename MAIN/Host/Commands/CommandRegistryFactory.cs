using System;
using VAL.Contracts;
using VAL.Host.Commands;

namespace VAL.Host
{
    internal static class CommandRegistryFactory
    {
        private static readonly string[] RequiredEnabled = { "enabled" };
        private static readonly string[] RequiredIndices = { "indices" };
        private static readonly string[] ContinuumTypes =
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

        public static void RegisterCommands(
            CommandRegistry registry,
            Action<HostCommand> handleContinuumCommand,
            Action<HostCommand> handleVoidSetEnabled,
            Action<HostCommand> handlePortalSetEnabled,
            Action<HostCommand> handlePortalOpenSnip,
            Action<HostCommand> handlePortalSendStaged,
            Action<HostCommand> handlePrivacySetContinuumLogging,
            Action<HostCommand> handlePrivacySetPortalCapture,
            Action<HostCommand> handlePrivacyOpenDataFolder,
            Action<HostCommand> handlePrivacyWipeData,
            Action<HostCommand> handleToolsOpenTruthHealth,
            Action<HostCommand> handleToolsOpenDiagnostics,
            Action<HostCommand> handleNavigationGoChat,
            Action<HostCommand> handleNavigationGoBack,
            Action<HostCommand> handleDockRequestModel,
            Action<HostCommand> handleDockUiStateGet,
            Action<HostCommand> handleDockUiStateSet,
            Action<HostCommand> handleAbyssOpenQueryUi,
            Action<HostCommand> handleAbyssSearch,
            Action<HostCommand> handleAbyssRetryLast,
            Action<HostCommand> handleAbyssInjectResult,
            Action<HostCommand> handleAbyssInjectResults,
            Action<HostCommand> handleAbyssLast,
            Action<HostCommand> handleAbyssOpenSource,
            Action<HostCommand> handleAbyssClearResults,
            Action<HostCommand> handleAbyssDisregard,
            Action<HostCommand> handleAbyssGetResults,
            Action<HostCommand> handleAbyssInjectPrompt,
            Action<HostCommand> handleAbyssInject)
        {
            ArgumentNullException.ThrowIfNull(registry);

            // ---- Void ----
            registry.Register(new CommandSpec(
                WebCommandNames.VoidCommandSetEnabled,
                "Void",
                RequiredEnabled,
                handleVoidSetEnabled
            ));

            // ---- Continuum ----
            // Explicit list so the host has a single discoverable map of supported commands.
            foreach (var t in ContinuumTypes)
            {
                registry.Register(new CommandSpec(
                    t,
                    "Continuum",
                    Array.Empty<string>(),
                    handleContinuumCommand
                ));
            }

            // ---- Abyss ----
            registry.Register(new CommandSpec(
                WebCommandNames.AbyssCommandOpenQueryUi,
                "Abyss",
                Array.Empty<string>(),
                handleAbyssOpenQueryUi
            ));

            registry.Register(new CommandSpec(
                WebCommandNames.AbyssCommandSearch,
                "Abyss",
                Array.Empty<string>(),
                handleAbyssSearch
            ));

            registry.Register(new CommandSpec(
                WebCommandNames.AbyssCommandRetryLast,
                "Abyss",
                Array.Empty<string>(),
                handleAbyssRetryLast
            ));

            registry.Register(new CommandSpec(
                WebCommandNames.AbyssCommandInjectResult,
                "Abyss",
                Array.Empty<string>(),
                handleAbyssInjectResult
            ));

            registry.Register(new CommandSpec(
                WebCommandNames.AbyssCommandInjectResults,
                "Abyss",
                RequiredIndices,
                handleAbyssInjectResults
            ));

            registry.Register(new CommandSpec(
                WebCommandNames.AbyssCommandLast,
                "Abyss",
                Array.Empty<string>(),
                handleAbyssLast
            ));

            registry.Register(new CommandSpec(
                WebCommandNames.AbyssCommandOpenSource,
                "Abyss",
                Array.Empty<string>(),
                handleAbyssOpenSource
            ));

            registry.Register(new CommandSpec(
                WebCommandNames.AbyssCommandClearResults,
                "Abyss",
                Array.Empty<string>(),
                handleAbyssClearResults
            ));

            registry.Register(new CommandSpec(
                WebCommandNames.AbyssCommandDisregard,
                "Abyss",
                Array.Empty<string>(),
                handleAbyssDisregard
            ));

            registry.Register(new CommandSpec(
                WebCommandNames.AbyssCommandGetResults,
                "Abyss",
                Array.Empty<string>(),
                handleAbyssGetResults
            ));

            registry.Register(new CommandSpec(
                WebCommandNames.AbyssCommandInjectPrompt,
                "Abyss",
                Array.Empty<string>(),
                handleAbyssInjectPrompt
            ));

            registry.Register(new CommandSpec(
                WebCommandNames.AbyssCommandInject,
                "Abyss",
                Array.Empty<string>(),
                handleAbyssInject
            ));

            // ---- Portal ----
            registry.Register(new CommandSpec(
                WebCommandNames.PortalCommandSetEnabled,
                "Portal",
                RequiredEnabled,
                handlePortalSetEnabled
            ));

            // Accept both command names (old/new) and map to same handler.
            registry.Register(new CommandSpec(
                WebCommandNames.PortalCommandOpenSnip,
                "Portal",
                Array.Empty<string>(),
                handlePortalOpenSnip
            ));

            registry.Register(new CommandSpec(
                WebCommandNames.PortalCommandOpenSnipOverlay,
                "Portal",
                Array.Empty<string>(),
                handlePortalOpenSnip
            ));

            registry.Register(new CommandSpec(
                WebCommandNames.PortalCommandSendStaged,
                "Portal",
                Array.Empty<string>(),
                handlePortalSendStaged
            ));

            registry.Register(new CommandSpec(
                WebCommandNames.PortalCommandSend,
                "Portal",
                Array.Empty<string>(),
                handlePortalSendStaged
            ));

            registry.Register(new CommandSpec(
                WebCommandNames.PortalCommandSendStagedLegacy,
                "Portal",
                Array.Empty<string>(),
                handlePortalSendStaged
            ));

            // ---- Privacy ----
            registry.Register(new CommandSpec(
                WebCommandNames.PrivacyCommandSetContinuumLogging,
                "Privacy",
                RequiredEnabled,
                handlePrivacySetContinuumLogging
            ));

            registry.Register(new CommandSpec(
                WebCommandNames.PrivacyCommandSetPortalCapture,
                "Privacy",
                RequiredEnabled,
                handlePrivacySetPortalCapture
            ));

            registry.Register(new CommandSpec(
                WebCommandNames.PrivacyCommandOpenDataFolder,
                "Privacy",
                Array.Empty<string>(),
                handlePrivacyOpenDataFolder
            ));

            registry.Register(new CommandSpec(
                WebCommandNames.PrivacyCommandWipeData,
                "Privacy",
                Array.Empty<string>(),
                handlePrivacyWipeData
            ));

            // ---- Tools ----
            registry.Register(new CommandSpec(
                WebCommandNames.ToolsOpenTruthHealth,
                "Tools",
                Array.Empty<string>(),
                handleToolsOpenTruthHealth
            ));

            registry.Register(new CommandSpec(
                WebCommandNames.ToolsOpenDiagnostics,
                "Tools",
                Array.Empty<string>(),
                handleToolsOpenDiagnostics
            ));

            // ---- Navigation ----
            // Deprecated/fenced: keep registered so host rejection is explicit and diagnosable.
            registry.Register(new CommandSpec(
                WebCommandNames.NavCommandGoChat,
                "Navigation",
                Array.Empty<string>(),
                handleNavigationGoChat,
                IsDeprecated: true,
                DeprecationReason: "Navigation command is deprecated in host command spine and intentionally fenced."
            ));

            registry.Register(new CommandSpec(
                WebCommandNames.NavCommandGoBack,
                "Navigation",
                Array.Empty<string>(),
                handleNavigationGoBack,
                IsDeprecated: true,
                DeprecationReason: "Navigation command is deprecated in host command spine and intentionally fenced."
            ));

            // ---- Dock ----
            registry.Register(new CommandSpec(
                WebCommandNames.DockCommandRequestModel,
                "Dock",
                Array.Empty<string>(),
                handleDockRequestModel
            ));

            registry.Register(new CommandSpec(
                WebCommandNames.DockUiStateGet,
                "Dock",
                Array.Empty<string>(),
                handleDockUiStateGet
            ));

            registry.Register(new CommandSpec(
                WebCommandNames.DockUiStateSet,
                "Dock",
                Array.Empty<string>(),
                handleDockUiStateSet
            ));
        }
    }
}
