using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using VAL.Continuum;
using VAL.Continuum.Pipeline;
using VAL.Host;

namespace VAL.Host.Services
{
    public sealed class PrivacySettingsService : IPrivacySettingsService
    {
        private const int CurrentVersion = 1;
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly object _gate = new();
        private readonly string _settingsPath;
        private PrivacySettingsState _settings;

        public PrivacySettingsService(IAppPaths appPaths)
        {
            ArgumentNullException.ThrowIfNull(appPaths);

            _settingsPath = Path.Combine(appPaths.DataRoot, "settings.json");
            _settings = LoadSettings();

            ApplyContinuumLogging(_settings.ContinuumLoggingEnabled);
        }

        public event Action<PrivacySettingsSnapshot>? SettingsChanged;

        public PrivacySettingsSnapshot GetSnapshot()
        {
            lock (_gate)
            {
                return _settings.ToSnapshot();
            }
        }

        public bool UpdateContinuumLogging(bool enabled)
        {
            var updated = Update(settings => settings.ContinuumLoggingEnabled = enabled);
            if (updated)
                ApplyContinuumLogging(enabled);
            return updated;
        }

        public bool UpdatePortalCapture(bool enabled)
        {
            return Update(settings => settings.PortalCaptureEnabled = enabled);
        }

        private PrivacySettingsState LoadSettings()
        {
            try
            {
                if (!File.Exists(_settingsPath))
                    return PrivacySettingsState.CreateDefault();

                var json = File.ReadAllText(_settingsPath);
                if (string.IsNullOrWhiteSpace(json))
                    return PrivacySettingsState.CreateDefault();

                var settings = JsonSerializer.Deserialize<PrivacySettingsState>(json, JsonOptions);
                if (settings == null)
                    return PrivacySettingsState.CreateDefault();

                if (settings.Version <= 0)
                    settings.Version = CurrentVersion;

                return settings.WithDefaults();
            }
            catch (Exception ex)
            {
                ValLog.Warn(nameof(PrivacySettingsService), $"Failed to load settings.json. {ex.GetType().Name}: {ex.Message}");
                return PrivacySettingsState.CreateDefault();
            }
        }

        private bool Update(Action<PrivacySettingsState> update)
        {
            PrivacySettingsSnapshot snapshot;
            bool changed;

            lock (_gate)
            {
                var next = _settings.Clone();
                update(next);
                next.Version = CurrentVersion;
                next = next.WithDefaults();

                changed = !next.Equals(_settings);
                if (changed)
                {
                    _settings = next;
                    PersistSettings(next);
                }

                snapshot = _settings.ToSnapshot();
            }

            if (changed)
            {
                SettingsChanged?.Invoke(snapshot);
            }

            return changed;
        }

        private void PersistSettings(PrivacySettingsState settings)
        {
            try
            {
                var json = JsonSerializer.Serialize(settings, JsonOptions);
                AtomicFile.WriteAllTextAtomic(_settingsPath, json);
            }
            catch (Exception ex)
            {
                ValLog.Warn(nameof(PrivacySettingsService), $"Failed to persist settings.json. {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void ApplyContinuumLogging(bool enabled)
        {
            try
            {
                ContinuumHost.ApplyLoggingSetting(enabled, showToast: false, reason: ToastReason.Background);
            }
            catch
            {
                ValLog.Warn(nameof(PrivacySettingsService), "Failed to apply Continuum logging setting.");
            }
        }

        private sealed class PrivacySettingsState : IEquatable<PrivacySettingsState>
        {
            [JsonPropertyName("version")]
            public int Version { get; set; } = CurrentVersion;

            [JsonPropertyName("continuumLoggingEnabled")]
            public bool ContinuumLoggingEnabled { get; set; } = true;

            [JsonPropertyName("portalCaptureEnabled")]
            public bool PortalCaptureEnabled { get; set; } = true;

            public static PrivacySettingsState CreateDefault()
            {
                return new PrivacySettingsState
                {
                    Version = CurrentVersion,
                    ContinuumLoggingEnabled = true,
                    PortalCaptureEnabled = true
                };
            }

            public PrivacySettingsState WithDefaults()
            {
                if (Version <= 0)
                    Version = CurrentVersion;

                return this;
            }

            public PrivacySettingsState Clone()
            {
                return new PrivacySettingsState
                {
                    Version = Version,
                    ContinuumLoggingEnabled = ContinuumLoggingEnabled,
                    PortalCaptureEnabled = PortalCaptureEnabled
                };
            }

            public PrivacySettingsSnapshot ToSnapshot()
            {
                return new PrivacySettingsSnapshot(Version, ContinuumLoggingEnabled, PortalCaptureEnabled);
            }

            public bool Equals(PrivacySettingsState? other)
            {
                if (other is null) return false;
                if (ReferenceEquals(this, other)) return true;
                return Version == other.Version
                    && ContinuumLoggingEnabled == other.ContinuumLoggingEnabled
                    && PortalCaptureEnabled == other.PortalCaptureEnabled;
            }

            public override bool Equals(object? obj)
            {
                return obj is PrivacySettingsState other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(Version, ContinuumLoggingEnabled, PortalCaptureEnabled);
            }
        }
    }
}
