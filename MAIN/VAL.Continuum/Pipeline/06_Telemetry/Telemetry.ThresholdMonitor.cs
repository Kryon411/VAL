using System;
using System.Collections.Concurrent;

namespace VAL.Continuum.Pipeline.Telemetry
{
    public enum ContinuumTelemetryThresholdLevel
    {
        Early = 1,
        Large = 2,
        VeryLarge = 3,
    }

    // TelemetryThresholdMonitor: emits progressive session-size nudges.
    // MUST be best-effort and never throw; Truth writing must always win.
    public sealed class TelemetryThresholdMonitor
    {
        // Thresholds are conservative and based on file size (Truth.log bytes).
        public const long SoftBytes = 250_000;
        public const long MediumBytes = 500_000;
        public const long CriticalBytes = 900_000;

        private sealed class ChatState
        {
            public int LastLevel;
        }

        private readonly ConcurrentDictionary<string, ChatState> _stateByChat =
            new ConcurrentDictionary<string, ChatState>(StringComparer.Ordinal);

        public event Action<string, ContinuumTelemetryThresholdLevel>? ThresholdReached;

        public void UpdateFromTruthBytes(string chatId, long bytes)
        {
            if (string.IsNullOrWhiteSpace(chatId)) return;
            if (bytes <= 0) return;

            try
            {
                var state = _stateByChat.GetOrAdd(chatId, _ => new ChatState());
                Evaluate(chatId, bytes, state);
            }
            catch
            {
                // telemetry must never throw
            }
        }

        private void Evaluate(string chatId, long bytes, ChatState state)
        {
            ContinuumTelemetryThresholdLevel? level = null;
            if (bytes >= CriticalBytes) level = ContinuumTelemetryThresholdLevel.VeryLarge;
            else if (bytes >= MediumBytes) level = ContinuumTelemetryThresholdLevel.Large;
            else if (bytes >= SoftBytes) level = ContinuumTelemetryThresholdLevel.Early;

            if (level == null)
                return;

            if ((int)level.Value <= state.LastLevel)
                return;

            // Update state first so repeated updates don't spam if the sink is unavailable.
            state.LastLevel = (int)level.Value;

            try
            {
                ThresholdReached?.Invoke(chatId, level.Value);
            }
            catch
            {
                // best-effort only
            }
        }
    }
}
