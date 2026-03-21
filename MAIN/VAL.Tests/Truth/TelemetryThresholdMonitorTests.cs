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

            TelemetryThresholdMonitor.Configure((id, level) => events.Add((id, level)));
            try
            {
                TelemetryThresholdMonitor.UpdateFromTruthBytes(chatId, TelemetryThresholdMonitor.SoftBytes);
                TelemetryThresholdMonitor.UpdateFromTruthBytes(chatId, TelemetryThresholdMonitor.MediumBytes);
                TelemetryThresholdMonitor.UpdateFromTruthBytes(chatId, TelemetryThresholdMonitor.CriticalBytes);

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
            finally
            {
                TelemetryThresholdMonitor.Configure(null);
            }
        }

        [Fact]
        public void UpdateFromTruthBytesDoesNotEmitBelowThresholdOrRepeatWithinBand()
        {
            var events = new List<(string ChatId, ContinuumTelemetryThresholdLevel Level)>();
            var chatId = Guid.NewGuid().ToString("N");

            TelemetryThresholdMonitor.Configure((id, level) => events.Add((id, level)));
            try
            {
                TelemetryThresholdMonitor.UpdateFromTruthBytes(chatId, TelemetryThresholdMonitor.SoftBytes - 1);
                TelemetryThresholdMonitor.UpdateFromTruthBytes(chatId, TelemetryThresholdMonitor.SoftBytes);
                TelemetryThresholdMonitor.UpdateFromTruthBytes(chatId, TelemetryThresholdMonitor.SoftBytes + 10);

                Assert.Single(events);
                Assert.Equal(chatId, events[0].ChatId);
                Assert.Equal(ContinuumTelemetryThresholdLevel.Early, events[0].Level);
            }
            finally
            {
                TelemetryThresholdMonitor.Configure(null);
            }
        }
    }
}
