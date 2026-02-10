using VAL.Contracts;
using VAL.Host.Security;
using Xunit;

namespace VAL.Tests.Security
{
    public sealed class WebCommandRegistryTests
    {
        [Fact]
        public void IsAllowedReturnsTrueForKnownType()
        {
            var result = WebCommandRegistry.IsAllowed(WebMessageTypes.Command);

            Assert.True(result);
        }

        [Fact]
        public void IsAllowedReturnsFalseForUnknownType()
        {
            var result = WebCommandRegistry.IsAllowed("unknown.command");

            Assert.False(result);
        }

        [Fact]
        public void IsAllowedReturnsFalseForUnknownContinuumType()
        {
            var result = WebCommandRegistry.IsAllowed("continuum.custom.command");

            Assert.False(result);
        }

        [Fact]
        public void IsAllowedReturnsTrueForDeprecatedNavigationType()
        {
            var result = WebCommandRegistry.IsAllowed(WebCommandNames.NavCommandGoChat);

            Assert.True(result);
        }
    }
}
