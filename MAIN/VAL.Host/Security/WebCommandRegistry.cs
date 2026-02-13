using System;
using System.Collections.Generic;
using VAL.Contracts;

namespace VAL.Host.Security
{
    public static class WebCommandRegistry
    {
        internal static readonly HashSet<string> AllowedTypes = new(StringComparer.Ordinal)
        {
            WebMessageTypes.Command,
            WebMessageTypes.Event,
            WebMessageTypes.Log,

            // ---- Void ----
            WebCommandNames.VoidCommandSetEnabled,

            // ---- Continuum ----
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

            // ---- Abyss ----
            WebCommandNames.AbyssCommandOpenQueryUi,
            WebCommandNames.AbyssCommandSearch,
            WebCommandNames.AbyssCommandRetryLast,
            WebCommandNames.AbyssCommandInjectResult,
            WebCommandNames.AbyssCommandInjectResults,
            WebCommandNames.AbyssCommandLast,
            WebCommandNames.AbyssCommandOpenSource,
            WebCommandNames.AbyssCommandClearResults,
            WebCommandNames.AbyssCommandDisregard,
            WebCommandNames.AbyssCommandGetResults,
            WebCommandNames.AbyssCommandInjectPrompt,
            WebCommandNames.AbyssCommandInject,

            // ---- Portal ----
            WebCommandNames.PortalCommandSetEnabled,
            WebCommandNames.PortalCommandOpenSnip,
            WebCommandNames.PortalCommandOpenSnipOverlay,
            WebCommandNames.PortalCommandSendStaged,
            WebCommandNames.PortalCommandSend,
            WebCommandNames.PortalCommandSendStagedLegacy,

            // ---- Privacy ----
            WebCommandNames.PrivacyCommandSetContinuumLogging,
            WebCommandNames.PrivacyCommandSetPortalCapture,
            WebCommandNames.PrivacyCommandOpenDataFolder,
            WebCommandNames.PrivacyCommandWipeData,

            // ---- Tools ----
            WebCommandNames.ToolsOpenTruthHealth,
            WebCommandNames.ToolsOpenDiagnostics,

            // ---- Navigation (deprecated, host-fenced) ----
            WebCommandNames.NavCommandGoChat,
            WebCommandNames.NavCommandGoBack,

            // ---- Dock ----
            WebCommandNames.DockCommandRequestModel,
            WebCommandNames.DockUiStateGet,
            WebCommandNames.DockUiStateSet,
        };

        public static bool IsAllowed(string type)
        {
            if (string.IsNullOrWhiteSpace(type))
                return false;

            if (AllowedTypes.Contains(type))
                return true;

            return false;
        }
    }
}
