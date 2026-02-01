using System;

namespace VAL.Host.Services
{
    public interface IPrivacySettingsService
    {
        event Action<PrivacySettingsSnapshot>? SettingsChanged;

        PrivacySettingsSnapshot GetSnapshot();

        bool UpdateContinuumLogging(bool enabled);

        bool UpdatePortalCapture(bool enabled);
    }

    public sealed record PrivacySettingsSnapshot(
        int Version,
        bool ContinuumLoggingEnabled,
        bool PortalCaptureEnabled);
}
