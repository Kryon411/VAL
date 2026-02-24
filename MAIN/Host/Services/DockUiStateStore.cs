using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VAL.Continuum.Pipeline.Common;
using VAL.Host.Json;

namespace VAL.Host.Services
{
    public interface IDockUiStateStore
    {
        DockUiState Load();
        void Save(DockUiState state);
    }

    public sealed class DockUiStateStore : IDockUiStateStore
    {
        private readonly string _productRoot;

        public DockUiStateStore(string? productRoot = null)
        {
            _productRoot = string.IsNullOrWhiteSpace(productRoot)
                ? ContinuumContext.ResolveProductRoot()
                : productRoot;

            if (string.IsNullOrWhiteSpace(_productRoot))
            {
                _productRoot = AppContext.BaseDirectory;
            }
        }

        public DockUiState Load()
        {
            try
            {
                var path = ResolveStatePath();
                if (!File.Exists(path))
                    return DockUiState.Default;

                var json = File.ReadAllText(path);
                var state = JsonSerializer.Deserialize<DockUiState>(json, ValJsonOptions.Default);
                if (state == null || state.Version != 1)
                    return DockUiState.Default;

                return state.WithUpdatedUtc();
            }
            catch
            {
                return DockUiState.Default;
            }
        }

        public void Save(DockUiState state)
        {
            try
            {
                var normalized = state.Version == 1 ? state.WithUpdatedUtc() : DockUiState.Default;
                var path = ResolveStatePath();
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var tempPath = path + ".tmp." + Guid.NewGuid().ToString("N");
                var json = JsonSerializer.Serialize(normalized, ValJsonOptions.Default);

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
                // Never throw from state persistence.
            }
        }

        private string ResolveStatePath()
        {
            return Path.Combine(_productRoot, "State", "dock.ui.json");
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
                // best effort
            }
        }
    }

    public sealed class DockUiState
    {
        [JsonPropertyName("version")]
        public int Version { get; set; } = 1;

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

        [JsonPropertyName("mode")]
        public string Mode { get; set; } = "floating";

        [JsonPropertyName("updatedUtc")]
        public string UpdatedUtc { get; set; } = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        public static DockUiState Default => new()
        {
            Version = 1,
            IsOpen = false,
            X = null,
            Y = null,
            W = null,
            H = null,
            Mode = "floating",
            UpdatedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
        };

        public DockUiState WithUpdatedUtc()
        {
            return new DockUiState
            {
                Version = 1,
                IsOpen = IsOpen,
                X = X,
                Y = Y,
                W = W,
                H = H,
                Mode = string.IsNullOrWhiteSpace(Mode) ? "floating" : Mode,
                UpdatedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
            };
        }
    }
}
