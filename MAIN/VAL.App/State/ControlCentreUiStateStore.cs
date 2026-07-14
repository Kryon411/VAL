using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

using VAL.Host.Services;

namespace VAL.App.State
{
    public interface IControlCentreUiStateStore
    {
        ControlCentreUiState Load();
        void Save(ControlCentreUiState state);
    }

    public sealed class ControlCentreUiStateStore : IControlCentreUiStateStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
        private const string CurrentStateFileName = "controlcentre.ui.json";
        private const string LegacyDockStateFileName = "dock.ui.json";
        private readonly string _stateRoot;

        public ControlCentreUiStateStore(IAppPaths appPaths)
        {
            ArgumentNullException.ThrowIfNull(appPaths);
            _stateRoot = appPaths.StateRoot;
        }

        public ControlCentreUiState Load()
        {
            try
            {
                var result = JsonStateFile.Read<ControlCentreUiState>(ResolveStatePath(), JsonOptions);
                if (result.IsSuccess && result.Value != null)
                    return result.Value.Normalize();

                var migrated = TryLoadLegacyDockState();
                return migrated?.Normalize() ?? ControlCentreUiState.Default;
            }
            catch
            {
                return ControlCentreUiState.Default;
            }
        }

        public void Save(ControlCentreUiState state)
        {
            ArgumentNullException.ThrowIfNull(state);

            try
            {
                var normalized = state.Normalize();
                JsonStateFile.Write(ResolveStatePath(), normalized, JsonOptions);
                TryDeleteLegacyDockState();
            }
            catch
            {
                // Never throw from shell state persistence.
            }
        }

        private string ResolveStatePath()
        {
            return Path.Combine(_stateRoot, CurrentStateFileName);
        }

        private string ResolveLegacyDockStatePath()
        {
            return Path.Combine(_stateRoot, LegacyDockStateFileName);
        }

        private ControlCentreUiState? TryLoadLegacyDockState()
        {
            var result = JsonStateFile.Read<LegacyDockUiState>(ResolveLegacyDockStatePath(), JsonOptions);
            if (!result.IsSuccess || result.Value == null || result.Value.Version != 1)
            {
                return null;
            }

            var state = ControlCentreUiState.Default;
            state.Dock.IsOpen = result.Value.IsOpen;

            if (result.Value.X.HasValue)
                state.Dock.X = result.Value.X.Value;

            if (result.Value.Y.HasValue)
                state.Dock.Y = result.Value.Y.Value;

            if (result.Value.W.HasValue)
                state.Dock.W = result.Value.W.Value;

            if (result.Value.H.HasValue)
                state.Dock.H = result.Value.H.Value;

            return state;
        }

        private void TryDeleteLegacyDockState()
        {
            try
            {
                var legacyPath = ResolveLegacyDockStatePath();
                if (File.Exists(legacyPath))
                    File.Delete(legacyPath);
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }

        private sealed class LegacyDockUiState
        {
            [JsonPropertyName("version")]
            public int Version { get; set; }

            [JsonPropertyName("isOpen")]
            public bool IsOpen { get; set; }

            [JsonPropertyName("x")]
            public int? X { get; set; }

            [JsonPropertyName("y")]
            public int? Y { get; set; }

            [JsonPropertyName("w")]
            public int? W { get; set; }

            [JsonPropertyName("h")]
            public int? H { get; set; }
        }
    }
}
