using System;
using System.Collections.Generic;
using VAL.Continuum.Pipeline.QuickRefresh;

namespace VAL.Host.Security
{
    internal static class WebCommandRegistry
    {
        internal static readonly HashSet<string> AllowedTypes = new(StringComparer.Ordinal)
        {
            "command",
            "event",
            "log",

            // ---- Void ----
            "void.command.set_enabled",

            // ---- Continuum ----
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

            // ---- Abyss ----
            "abyss.command.open_query_ui",
            "abyss.command.search",
            "abyss.command.retry_last",
            "abyss.command.inject_result",
            "abyss.command.inject_results",
            "abyss.command.last",
            "abyss.command.open_source",
            "abyss.command.clear_results",
            "abyss.command.disregard",
            "abyss.command.get_results",
            "abyss.command.inject_prompt",
            "abyss.command.inject",

            // ---- Portal ----
            "portal.command.set_enabled",
            "portal.command.open_snip",
            "portal.command.open_snip_overlay",
            "portal.command.send_staged",
            "portal.command.send",
            "portal.command.sendStaged",

            // ---- Tools ----
            "tools.open_truth_health",
            "tools.open_diagnostics",
        };

        internal static bool IsAllowed(string type)
        {
            if (string.IsNullOrWhiteSpace(type))
                return false;

            if (AllowedTypes.Contains(type))
                return true;

            return false;
        }
    }
}
