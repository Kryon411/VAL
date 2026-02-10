using System;
using System.Threading;
using VAL.Host.Logging;
using Xunit;

namespace VAL.Tests.Logging
{
    public sealed class RateLimiterTests
    {
        [Fact]
        public void AllowReturnsTrueThenFalseWithinInterval()
        {
            var limiter = new RateLimiter();
            var interval = TimeSpan.FromMilliseconds(200);

            Assert.True(limiter.Allow("key", interval));
            Assert.False(limiter.Allow("key", interval));
        }

        [Fact]
        public void AllowReturnsTrueAfterInterval()
        {
            var limiter = new RateLimiter();
            var interval = TimeSpan.FromMilliseconds(100);

            Assert.True(limiter.Allow("key", interval));
            Assert.False(limiter.Allow("key", interval));

            Thread.Sleep(150);

            Assert.True(limiter.Allow("key", interval));
        }
    }
}
