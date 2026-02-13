using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;

namespace VAL.Contracts
{
    /// <summary>
    /// Canonical web command names shared by host + modules.
    /// </summary>
    public static class WebCommandNames
    {
        // ---- Void ----
        public const string VoidCommandSetEnabled = "void.command.set_enabled";

        // ---- Continuum ----
        public const string ContinuumCaptureFlushAck = "continuum.capture.flush_ack";
        public const string ContinuumCaptureFlush = "continuum.capture.flush";
        public const string ContinuumSessionAttach = "continuum.session.attach";
        public const string ContinuumSessionAttached = "continuum.session.attached";
        public const string ContinuumCommandToggleLogging = "continuum.command.toggle_logging";
        public const string ContinuumUiNewChat = "continuum.ui.new_chat";
        public const string ContinuumUiPreludePrompt = "continuum.ui.prelude_prompt";
        public const string ContinuumUiComposerInteraction = "continuum.ui.composer_interaction";
        public const string ContinuumCommandInjectPreamble = "continuum.command.inject_preamble";
        public const string ContinuumCommandInjectPrelude = "continuum.command.inject_prelude";
        public const string ContinuumTruthAppend = "continuum.truth.append";
        public const string TruthAppend = "truth.append";
        public const string ContinuumTruth = "continuum.truth";
        public const string ContinuumCommandPulse = "continuum.command.pulse";
        public const string ContinuumCommandRefreshQuick = "continuum.command.refresh_quick";
        public const string ContinuumCommandOpenSessionFolder = "continuum.command.open_session_folder";
        public const string ContinuumCommandChronicleCancel = "continuum.command.chronicle_cancel";
        public const string ContinuumCommandCancelChronicle = "continuum.command.cancel_chronicle";
        public const string ContinuumCommandChronicleRebuildTruth = "continuum.command.chronicle_rebuild_truth";
        public const string ContinuumCommandChronicle = "continuum.command.chronicle";
        public const string ContinuumChronicleProgress = "continuum.chronicle.progress";
        public const string ContinuumChronicleDone = "continuum.chronicle.done";
        public const string ContinuumChronicleCancel = "continuum.chronicle.cancel";
        public const string ContinuumChronicleStart = "continuum.chronicle.start";
        public const string InjectSuccess = "inject.success";
        public const string ContinuumEvent = "continuum.event";
        public const string ContinuumInjectText = "continuum.inject_text";

        // ---- Abyss ----
        public const string AbyssCommandOpenQueryUi = "abyss.command.open_query_ui";
        public const string AbyssCommandSearch = "abyss.command.search";
        public const string AbyssCommandRetryLast = "abyss.command.retry_last";
        public const string AbyssCommandInjectResult = "abyss.command.inject_result";
        public const string AbyssCommandInjectResults = "abyss.command.inject_results";
        public const string AbyssCommandLast = "abyss.command.last";
        public const string AbyssCommandOpenSource = "abyss.command.open_source";
        public const string AbyssCommandClearResults = "abyss.command.clear_results";
        public const string AbyssCommandDisregard = "abyss.command.disregard";
        public const string AbyssCommandGetResults = "abyss.command.get_results";
        public const string AbyssCommandInjectPrompt = "abyss.command.inject_prompt";
        public const string AbyssCommandInject = "abyss.command.inject";

        // ---- Portal ----
        public const string PortalCommandSetEnabled = "portal.command.set_enabled";
        public const string PortalCommandOpenSnip = "portal.command.open_snip";
        public const string PortalCommandOpenSnipOverlay = "portal.command.open_snip_overlay";
        public const string PortalCommandSendStaged = "portal.command.send_staged";
        public const string PortalCommandSend = "portal.command.send";
        public const string PortalCommandSendStagedLegacy = "portal.command.sendStaged";
        public const string PortalState = "portal.state";

        // ---- Privacy ----
        public const string PrivacyCommandSetContinuumLogging = "privacy.command.set_continuum_logging";
        public const string PrivacyCommandSetPortalCapture = "privacy.command.set_portal_capture";
        public const string PrivacyCommandOpenDataFolder = "privacy.command.open_data_folder";
        public const string PrivacyCommandWipeData = "privacy.command.wipe_data";

        // ---- Tools ----
        public const string ToolsOpenTruthHealth = "tools.open_truth_health";
        public const string ToolsOpenDiagnostics = "tools.open_diagnostics";

        // ---- Navigation ----
        public const string NavCommandGoChat = "nav.command.go_chat";
        public const string NavCommandGoBack = "nav.command.go_back";

        // ---- Dock ----
        public const string DockCommandRequestModel = "dock.command.request_model";
        public const string DockUiStateGet = "dock.ui_state.get";
        public const string DockUiStateSet = "dock.ui_state.set";
        public const string DockModel = "val.dock.model";

        private static readonly IReadOnlyDictionary<string, string> AllCommands = BuildAllCommands();

        public static IReadOnlyDictionary<string, string> GetAll()
        {
            return AllCommands;
        }

        private static ReadOnlyDictionary<string, string> BuildAllCommands()
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            var fields = typeof(WebCommandNames).GetFields(BindingFlags.Public | BindingFlags.Static);
            foreach (var field in fields)
            {
                if (field.FieldType != typeof(string))
                    continue;

                if (!field.IsLiteral || field.IsInitOnly)
                    continue;

                var value = field.GetRawConstantValue() as string;
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                result[field.Name] = value;
            }

            return new ReadOnlyDictionary<string, string>(result);
        }
    }
}
