using System;
using VAL.Contracts;

namespace VAL.Continuum.Pipeline.QuickRefresh
{
    public static class QuickRefreshCommands
    {
        // Web -> Host command (ModuleDock / client)
        public const string CommandPulse = WebCommandNames.ContinuumCommandPulse;
        // Legacy alias (pre-Pulse docks)
        public const string CommandRefreshQuick = WebCommandNames.ContinuumCommandRefreshQuick;
        public const string CommandInjectPreamble = WebCommandNames.ContinuumCommandInjectPreamble;

        // Host -> Web inject contract (SEALED by client injector (Continuum.Client.js))
        public const string InjectType = WebCommandNames.ContinuumInjectText;
    }
}
