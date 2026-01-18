namespace VAL.Host
{
    // Central toast keys used by ToastHub policy + routing.
    public enum ToastKey
    {
        // Host / global
        VoidEnabled,
        VoidDisabled,

        // Continuum lifecycle
        ContinuumArchivingPaused,

        // Continuum guidance
        PreludeAvailable,
        PreludePrompt,
        ChroniclePrompt,
        ChronicleSuggested,

        // Pulse
        PulseInitiated,
        PulseReady,
        PulseAlreadyRunning,
        PulseUnavailable,
        PulseNoTruthLogFound,

        // Chronicle
        ChronicleUnavailable,
        ChronicleStarted,
        ChronicleCompleted,

        // Abyss recall/search
        AbyssSearching,
        AbyssMatches,
        AbyssNoMatches,
        AbyssNoTruthLogs,
        AbyssNoQuery,
        AbyssResultsWritten,
        AbyssInjected,
        AbyssNoSelection,

        // Generic guardrails
        ActionUnavailable,
        OperationInProgress,
        OperationCancelled,

        // Telemetry
        TelemetrySessionSizeEarly,
        TelemetrySessionSizeLarge,
        TelemetrySessionSizeVeryLarge,
    }
}
