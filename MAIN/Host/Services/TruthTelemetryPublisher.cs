using System;
using VAL.Continuum.Pipeline.Telemetry;
using VAL.Continuum.Pipeline.Truth;

namespace VAL.Host.Services
{
    internal sealed class TruthTelemetryPublisher : ITruthTelemetryPublisher
    {
        private readonly TelemetryThresholdMonitor _monitor;

        public TruthTelemetryPublisher(TelemetryThresholdMonitor monitor)
        {
            _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        }

        public void PublishTruthBytes(string chatId, long bytes)
        {
            _monitor.UpdateFromTruthBytes(chatId, bytes);
        }
    }
}
