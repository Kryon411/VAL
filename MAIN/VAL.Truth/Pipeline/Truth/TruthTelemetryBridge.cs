using System;

namespace VAL.Continuum.Pipeline.Truth
{
    internal static class TruthTelemetryBridge
    {
        private static Action<string, long>? _sink;

        public static void Configure(Action<string, long>? sink)
        {
            _sink = sink;
        }

        public static void Publish(string chatId, long bytes)
        {
            if (string.IsNullOrWhiteSpace(chatId))
                return;

            try
            {
                _sink?.Invoke(chatId, bytes);
            }
            catch
            {
                // telemetry must never throw
            }
        }
    }
}
