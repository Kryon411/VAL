using System;

using VAL.App.Host.Services;

using Xunit;

namespace VAL.Tests.Hosting
{
    public sealed class DesktopToastBurstGateTests
    {
        [Fact]
        public void ShouldSuppressReturnsFalseThenTrueWithinBurstWindow()
        {
            var gate = new DesktopToastBurstGate(TimeSpan.FromSeconds(2));
            var nowUtc = new DateTime(2026, 7, 13, 12, 0, 0, DateTimeKind.Utc);

            var first = gate.ShouldSuppress("Toast", "Subtitle", nowUtc);
            var second = gate.ShouldSuppress("Toast", "Subtitle", nowUtc.AddSeconds(1));

            Assert.False(first);
            Assert.True(second);
        }

        [Fact]
        public void ShouldSuppressAllowsToastAgainAfterBurstWindowExpires()
        {
            var gate = new DesktopToastBurstGate(TimeSpan.FromSeconds(2));
            var nowUtc = new DateTime(2026, 7, 13, 12, 0, 0, DateTimeKind.Utc);

            _ = gate.ShouldSuppress("Toast", "Subtitle", nowUtc);
            var allowedAgain = gate.ShouldSuppress("Toast", "Subtitle", nowUtc.AddSeconds(3));

            Assert.False(allowedAgain);
        }

        [Fact]
        public void ShouldSuppressTrimsTitleAndSubtitleBeforeComparing()
        {
            var gate = new DesktopToastBurstGate(TimeSpan.FromSeconds(2));
            var nowUtc = new DateTime(2026, 7, 13, 12, 0, 0, DateTimeKind.Utc);

            _ = gate.ShouldSuppress("  Toast  ", "  Subtitle  ", nowUtc);
            var suppressed = gate.ShouldSuppress("Toast", "Subtitle", nowUtc.AddMilliseconds(500));

            Assert.True(suppressed);
        }

        [Fact]
        public void ShouldSuppressTreatsDifferentSubtitlesAsDifferentToasts()
        {
            var gate = new DesktopToastBurstGate(TimeSpan.FromSeconds(2));
            var nowUtc = new DateTime(2026, 7, 13, 12, 0, 0, DateTimeKind.Utc);

            _ = gate.ShouldSuppress("Toast", "Subtitle A", nowUtc);
            var suppressed = gate.ShouldSuppress("Toast", "Subtitle B", nowUtc.AddMilliseconds(500));

            Assert.False(suppressed);
        }
    }
}
