using System;

namespace VAL.Continuum.Pipeline.QuickRefresh
{
    public static class QuickRefreshCommands
    {
        // Web -> Host command (ModuleDock / client)
        public const string CommandPulse = "continuum.command.pulse";
        // Legacy alias (pre-Pulse docks)
        public const string CommandRefreshQuick = "continuum.command.refresh_quick";
        public const string CommandInjectPreamble = "continuum.command.inject_preamble";

        // Host -> Web inject contract (SEALED by client injector (Continuum.Client.js))
        public const string InjectType = "continuum.inject_text";
    }
}
