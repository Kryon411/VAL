using VAL.App.Host.Services;

using Xunit;

namespace VAL.Tests.Hosting
{
    public sealed class SafeBootTests
    {
        [Fact]
        public void ResolveFatalExitCodeReturnsSmokeFailureCode()
        {
            Assert.Equal(40, SafeBoot.ResolveFatalExitCode(smokeTestEnabled: true));
        }

        [Fact]
        public void ResolveFatalExitCodeReturnsStandardFailureCode()
        {
            Assert.Equal(1, SafeBoot.ResolveFatalExitCode(smokeTestEnabled: false));
        }
    }
}
