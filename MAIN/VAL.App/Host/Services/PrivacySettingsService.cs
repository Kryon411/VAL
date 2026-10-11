using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

using VAL.Continuum;

namespace VAL.App.Host.Services
{
    internal sealed class PrivacySettingsService : IPrivacySettingsService
    {
        private const int CurrentVersion = 1;
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly Action<bool> _applyLogging;
        private readonly ILog _log;
        private readonly object _gate = new();
        private readonly string _settingsPath;
        private PrivacySettingsState _settings;

        public PrivacySettingsService(IAppPaths appPaths, ContinuumHost continuumHost, ILog log)
            : this(appPaths, CreateLoggingSetter(continuumHost), log)
        {
        }

        internal PrivacySettingsService(IAppPaths appPaths, Action<bool> applyLogging, ILog log)
        {
            ArgumentNullException.ThrowIfNull(appPaths);

            _applyLogging = applyLogging ?? throw new ArgumentNullException(nameof(applyLogging));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _settingsPath = Path.Combine(appPaths.DataRoot, "settings.json");
            _settings = LoadSettings();

            ApplyContinuumLogging(_settings.ContinuumLoggingEnabled);
        }

        private static Action<bool> CreateLoggingSetter(ContinuumHost host)
        {
            ArgumentNullException.ThrowIfNull(host);
            return enabled => host.ApplyLoggingSetting(enabled, showToast: false);
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
            return Update(settings => settings.ContinuumLoggingEnabled = enabled, applyLogging: true);
        }

        public bool UpdatePortalCapture(bool enabled)
        {
            return Update(settings => settings.PortalCaptureEnabled = enabled);
        }

        public bool DisableCapture() => Update(settings =>
        {
            settings.ContinuumLoggingEnabled = false;
            settings.PortalCaptureEnabled = false;
        }, applyLogging: true);

        private PrivacySettingsState LoadSettings()
        {
            try
            {
                var result = JsonStateFile.Read<PrivacySettingsState>(_settingsPath, JsonOptions);
                if (result.Status == JsonStateFileReadStatus.Missing)
                    return PrivacySettingsState.CreateDefault();

                if (!result.IsSuccess)
                {
                    var errorType = result.Error?.GetType().Name ?? "InvalidData";
                    var errorMessage = result.Error?.Message;
                    var suffix = string.IsNullOrWhiteSpace(errorMessage)
                        ? errorType
                        : $"{errorType}: {errorMessage}";
                    _log.Warn(nameof(PrivacySettingsService), $"Failed to load settings.json. {suffix}");
                    return PrivacySettingsState.CreateDisabled();
                }

                var settings = result.Value!;
                if (settings.Version != CurrentVersion)
                    return PrivacySettingsState.CreateDisabled();

                return settings.WithDefaults();
            }
            catch (Exception ex)
            {
                _log.Warn(nameof(PrivacySettingsService), $"Failed to load settings.json. {ex.GetType().Name}: {ex.Message}");
                return PrivacySettingsState.CreateDisabled();
            }
        }

        private bool Update(Action<PrivacySettingsState> update, bool applyLogging = false)
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
                    PersistSettings(next);
                    _settings = next;
                    if (applyLogging)
                        ApplyContinuumLogging(next.ContinuumLoggingEnabled);
                }

                snapshot = _settings.ToSnapshot();
            }

            if (changed)
            {
                if (SettingsChanged != null)
                {
                    foreach (var handler in SettingsChanged.GetInvocationList())
                    {
                        try { ((Action<PrivacySettingsSnapshot>)handler)(snapshot); }
                        catch { _log.Warn(nameof(PrivacySettingsService), "Privacy settings subscriber failed."); }
                    }
                }
            }

            return changed;
        }

        private void PersistSettings(PrivacySettingsState settings)
        {
            try
            {
                JsonStateFile.Write(_settingsPath, settings, JsonOptions);
            }
            catch (Exception ex)
            {
                _log.Warn(nameof(PrivacySettingsService), $"Failed to persist settings.json. {ex.GetType().Name}: {ex.Message}");
                throw;
            }
        }

        private void ApplyContinuumLogging(bool enabled)
        {
            try
            {
                _applyLogging(enabled);
            }
            catch
            {
                _log.Warn(nameof(PrivacySettingsService), "Failed to apply Continuum logging setting.");
            }
        }

        private sealed class PrivacySettingsState : IEquatable<PrivacySettingsState>
        {
            [JsonPropertyName("version")]
            [JsonRequired]
            public int Version { get; set; } = CurrentVersion;

            [JsonPropertyName("continuumLoggingEnabled")]
            [JsonRequired]
            public bool ContinuumLoggingEnabled { get; set; } = true;

            [JsonPropertyName("portalCaptureEnabled")]
            [JsonRequired]
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

            public static PrivacySettingsState CreateDisabled() => new()
            {
                ContinuumLoggingEnabled = false,
                PortalCaptureEnabled = false,
            };

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
