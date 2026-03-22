using System;
using System.Collections.Generic;
using VAL.Continuum.Pipeline.Telemetry;
using Xunit;

namespace VAL.Tests.Truth
{
    public sealed class TelemetryThresholdMonitorTests
    {
        [Fact]
        public void UpdateFromTruthBytesEmitsThresholdCallbacksOnUpwardCrossings()
        {
            var events = new List<(string ChatId, ContinuumTelemetryThresholdLevel Level)>();
            var chatId = Guid.NewGuid().ToString("N");
            var monitor = new TelemetryThresholdMonitor();

            monitor.ThresholdReached += (id, level) => events.Add((id, level));
            monitor.UpdateFromTruthBytes(chatId, TelemetryThresholdMonitor.SoftBytes);
            monitor.UpdateFromTruthBytes(chatId, TelemetryThresholdMonitor.MediumBytes);
            monitor.UpdateFromTruthBytes(chatId, TelemetryThresholdMonitor.CriticalBytes);

            Assert.Collection(
                events,
                item =>
                {
                    Assert.Equal(chatId, item.ChatId);
                    Assert.Equal(ContinuumTelemetryThresholdLevel.Early, item.Level);
                },
                item =>
                {
                    Assert.Equal(chatId, item.ChatId);
                    Assert.Equal(ContinuumTelemetryThresholdLevel.Large, item.Level);
                },
                item =>
                {
                    Assert.Equal(chatId, item.ChatId);
                    Assert.Equal(ContinuumTelemetryThresholdLevel.VeryLarge, item.Level);
                });
        }

        [Fact]
        public void UpdateFromTruthBytesDoesNotEmitBelowThresholdOrRepeatWithinBand()
        {
            var events = new List<(string ChatId, ContinuumTelemetryThresholdLevel Level)>();
            var chatId = Guid.NewGuid().ToString("N");
            var monitor = new TelemetryThresholdMonitor();

            monitor.ThresholdReached += (id, level) => events.Add((id, level));
            monitor.UpdateFromTruthBytes(chatId, TelemetryThresholdMonitor.SoftBytes - 1);
            monitor.UpdateFromTruthBytes(chatId, TelemetryThresholdMonitor.SoftBytes);
            monitor.UpdateFromTruthBytes(chatId, TelemetryThresholdMonitor.SoftBytes + 10);

            Assert.Single(events);
            Assert.Equal(chatId, events[0].ChatId);
            Assert.Equal(ContinuumTelemetryThresholdLevel.Early, events[0].Level);
        }
    }
}
