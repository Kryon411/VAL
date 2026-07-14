using System;

using VAL.App.Host.Services;
using VAL.Host.Services;

using Xunit;

namespace VAL.Tests.Hosting
{
    public sealed class DesktopToastDurationPolicyTests
    {
        [Theory]
        [InlineData(ToastDuration.XS, 2)]
        [InlineData(ToastDuration.S, 5)]
        [InlineData(ToastDuration.M, 10)]
        [InlineData(ToastDuration.L, 14)]
        [InlineData(ToastDuration.XL, 22)]
        public void ResolveReturnsExpectedLifetimeForTimedDurations(ToastDuration duration, int expectedSeconds)
        {
            var lifetime = DesktopToastDurationPolicy.Resolve(duration);

            Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), lifetime);
        }

        [Fact]
        public void ResolveReturnsNullForStickyToasts()
        {
            var lifetime = DesktopToastDurationPolicy.Resolve(ToastDuration.Sticky);

            Assert.Null(lifetime);
        }
    }
}
