using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using VAL.Continuum.Pipeline.Common;
using VAL.Host.Logging;

namespace VAL.Host.Startup
{
    public sealed class StartupCrashGuard
    {
        private const string Category = "StartupCrashGuard";
        private const int CrashThreshold = 2;
        private static readonly TimeSpan LogInterval = TimeSpan.FromSeconds(30);
        private static readonly RateLimiter RateLimiter = new();
        private readonly string _productRoot;

        public StartupCrashGuard(string? productRoot = null)
        {
            _productRoot = string.IsNullOrWhiteSpace(productRoot)
                ? ContinuumContext.ResolveProductRoot()
                : productRoot;

            if (string.IsNullOrWhiteSpace(_productRoot))
            {
                _productRoot = AppContext.BaseDirectory;
            }
        }

        public bool EvaluateAndMarkStarting()
        {
            var shouldEnterSafeMode = false;
            var state = ReadState(out var stateValid);

            if (!stateValid || state == null)
            {
                shouldEnterSafeMode = true;
                state = new StartupCrashState();
            }
            else if (state.ConsecutiveStartupCrashes >= CrashThreshold)
            {
                shouldEnterSafeMode = true;
            }

            state.ConsecutiveStartupCrashes = Math.Max(0, state.ConsecutiveStartupCrashes) + 1;
            state.LastStartUtc = DateTime.UtcNow.ToString("O");
            WriteState(state);

            return shouldEnterSafeMode;
        }

        public void MarkSuccess()
        {
            try
            {
                var state = ReadState(out _)
                    ?? new StartupCrashState();

                state.ConsecutiveStartupCrashes = 0;
                state.LastSuccessUtc = DateTime.UtcNow.ToString("O");
                WriteState(state);
            }
            catch
            {
                // Never throw from crash guard.
            }
        }

        private string ResolveStatePath()
        {
            return Path.Combine(_productRoot, "State", "startup.json");
        }

        private StartupCrashState? ReadState(out bool stateValid)
        {
            stateValid = false;

            try
            {
                var path = ResolveStatePath();
                if (!File.Exists(path))
                    return null;

                var json = File.ReadAllText(path);
                var state = JsonSerializer.Deserialize<StartupCrashState>(json);
                if (state == null)
                {
                    LogStateIssue("Startup crash guard state was empty; entering safe mode.");
                    return null;
                }

                stateValid = true;
                return state;
            }
            catch (Exception ex)
            {
                LogStateIssue($"Startup crash guard state unreadable; entering safe mode. {ex.GetType().Name}");
                return null;
            }
        }

        private void WriteState(StartupCrashState state)
        {
            try
            {
                var path = ResolveStatePath();
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                LogStateIssue($"Failed to persist startup crash state. {ex.GetType().Name}");
            }
        }

        private static void LogStateIssue(string message)
        {
            if (RateLimiter.Allow("startup.guard.state", LogInterval))
            {
                ValLog.Warn(Category, message);
            }
        }

        private sealed class StartupCrashState
        {
            [JsonPropertyName("consecutiveStartupCrashes")]
            public int ConsecutiveStartupCrashes { get; set; }

            [JsonPropertyName("lastStartUtc")]
            public string? LastStartUtc { get; set; }

            [JsonPropertyName("lastSuccessUtc")]
            public string? LastSuccessUtc { get; set; }
        }
    }
}
