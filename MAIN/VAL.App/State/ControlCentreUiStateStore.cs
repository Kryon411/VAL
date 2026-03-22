using System;
using System.IO;
using System.Text;
using System.Text.Json;
using VAL.Host.Services;

namespace VAL
{
    public interface IControlCentreUiStateStore
    {
        ControlCentreUiState Load();
        void Save(ControlCentreUiState state);
    }

    public sealed class ControlCentreUiStateStore : IControlCentreUiStateStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
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
                var path = ResolveStatePath();
                if (!File.Exists(path))
                    return ControlCentreUiState.Default;

                var json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<ControlCentreUiState>(json, JsonOptions);
                return loaded?.Normalize() ?? ControlCentreUiState.Default;
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
                var path = ResolveStatePath();
                Directory.CreateDirectory(_stateRoot);

                var tempPath = path + ".tmp." + Guid.NewGuid().ToString("N");
                var json = JsonSerializer.Serialize(normalized, JsonOptions);

                try
                {
                    using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                    using (var sw = new StreamWriter(fs, new UTF8Encoding(false)))
                    {
                        sw.Write(json);
                        sw.Flush();
                        fs.Flush(flushToDisk: true);
                    }

                    if (File.Exists(path))
                    {
                        File.Replace(tempPath, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
                    }
                    else
                    {
                        File.Move(tempPath, path);
                    }
                }
                finally
                {
                    TryDelete(tempPath);
                }
            }
            catch
            {
                // Never throw from shell state persistence.
            }
        }

        private string ResolveStatePath()
        {
            return Path.Combine(_stateRoot, "controlcentre.ui.json");
        }

        private static void TryDelete(string? path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Best effort cleanup only.
            }
        }
    }
}
