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

        // Generic guardrails
        ActionUnavailable,
        OperationInProgress,
        OperationCancelled,

        // Telemetry
        TelemetrySessionSizeEarly,
        TelemetrySessionSizeLarge,
        TelemetrySessionSizeVeryLarge,

        // Abyss
        AbyssSearching,
        AbyssMatches,
        AbyssInjected,
        AbyssResultsWritten,
        AbyssNoMatches,
        AbyssNoTruthLogs,
        AbyssQueryPrompt,
    }
}
