using System;
using System.Collections.Concurrent;
using VAL.Host;

namespace VAL.Continuum.Pipeline.Telemetry
{
    // TelemetryThresholdMonitor: emits progressive session-size nudges.
    // MUST be best-effort and never throw; Truth writing must always win.
    public static class TelemetryThresholdMonitor
    {
        // Thresholds are conservative and based on file size (Truth.log bytes).
        public const long SoftBytes = 250_000;
        public const long MediumBytes = 500_000;
        public const long CriticalBytes = 900_000;

        private sealed class ChatState
        {
            public int LastLevel;
        }

        private static readonly ConcurrentDictionary<string, ChatState> _stateByChat =
            new ConcurrentDictionary<string, ChatState>(StringComparer.Ordinal);

        public static void UpdateFromTruthBytes(string chatId, long bytes)
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

        private static void Evaluate(string chatId, long bytes, ChatState state)
        {
            int level = 0;
            if (bytes >= CriticalBytes) level = 3;
            else if (bytes >= MediumBytes) level = 2;
            else if (bytes >= SoftBytes) level = 1;

            if (level <= state.LastLevel)
                return;

            // Update state first so repeated updates don't spam if toast/UI is unavailable.
            state.LastLevel = level;

            try
            {
                switch (level)
                {
                    case 1:
                        ToastHub.TryShow(
                            ToastKey.TelemetrySessionSizeEarly,
                            chatId,
                            origin: ToastOrigin.Telemetry,
                            reason: ToastReason.Background);
                        break;

                    case 2:
                        ToastHub.TryShow(
                            ToastKey.TelemetrySessionSizeLarge,
                            chatId,
                            origin: ToastOrigin.Telemetry,
                            reason: ToastReason.Background);
                        break;

                    case 3:
                        ToastHub.TryShow(
                            ToastKey.TelemetrySessionSizeVeryLarge,
                            chatId,
                            origin: ToastOrigin.Telemetry,
                            reason: ToastReason.Background);
                        break;
                }
            }
            catch
            {
                // best-effort only
            }
        }
    }
}
