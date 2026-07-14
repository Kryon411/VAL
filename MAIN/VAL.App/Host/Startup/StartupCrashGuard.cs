using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

using VAL.Host.Json;
using VAL.Host.Logging;
using VAL.Host.Options;
using VAL.Host.Services;

namespace VAL.App.Host.Startup
{
    public sealed class StartupCrashGuard : IStartupCrashGuard
    {
        private const string Category = "StartupCrashGuard";
        private const int CrashThreshold = 2;
        private static readonly TimeSpan LogInterval = TimeSpan.FromSeconds(30);
        private readonly string _dataRoot;
        private readonly ILog _log;
        private readonly RateLimiter _rateLimiter = new();

        public StartupCrashGuard(ILog log, string? dataRoot = null)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _dataRoot = string.IsNullOrWhiteSpace(dataRoot)
                ? ValOptions.DefaultDataRoot
                : dataRoot;

            if (string.IsNullOrWhiteSpace(_dataRoot))
            {
                _dataRoot = ValOptions.DefaultDataRoot;
            }
        }

        public bool EvaluateAndMarkStarting()
        {
            var state = ReadState(out var readStatus) ?? new StartupCrashState();
            var shouldEnterSafeMode = readStatus == StartupStateReadStatus.Invalid ||
                                      state.ConsecutiveStartupCrashes >= CrashThreshold;

            state.ConsecutiveStartupCrashes = Math.Max(0, state.ConsecutiveStartupCrashes) + 1;
            state.LastStartUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
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
                state.LastSuccessUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                WriteState(state);
            }
            catch
            {
                // Never throw from crash guard.
            }
        }

        private string ResolveStatePath()
        {
            return Path.Combine(_dataRoot, "State", "startup.json");
        }

        private StartupCrashState? ReadState(out StartupStateReadStatus status)
        {
            status = StartupStateReadStatus.Invalid;

            try
            {
                var result = JsonStateFile.Read<StartupCrashState>(ResolveStatePath(), ValJsonOptions.Default);
                if (result.Status == JsonStateFileReadStatus.Missing)
                {
                    status = StartupStateReadStatus.Missing;
                    return null;
                }

                if (result.Status == JsonStateFileReadStatus.Empty)
                {
                    LogStateIssue("Startup crash guard state was empty; entering safe mode.");
                    return null;
                }

                if (!result.IsSuccess)
                {
                    var errorType = result.Error?.GetType().Name ?? "InvalidData";
                    LogStateIssue($"Startup crash guard state unreadable; entering safe mode. {errorType}");
                    return null;
                }

                status = StartupStateReadStatus.Valid;
                return result.Value;
            }
            catch (Exception ex)
            {
                LogStateIssue($"Startup crash guard state unreadable; entering safe mode. {ex.GetType().Name}");
                return null;
            }
        }

        private enum StartupStateReadStatus
        {
            Missing,
            Valid,
            Invalid
        }

        private void WriteState(StartupCrashState state)
        {
            try
            {
                JsonStateFile.Write(ResolveStatePath(), state, ValJsonOptions.Default);
            }
            catch (Exception ex)
            {
                LogStateIssue($"Failed to persist startup crash state. {ex.GetType().Name}");
            }
        }

        private void LogStateIssue(string message)
        {
            if (_rateLimiter.Allow("startup.guard.state", LogInterval))
            {
                _log.Warn(Category, message);
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
