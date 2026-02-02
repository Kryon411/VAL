using VAL.Contracts;
using VAL.Host.Security;
using Xunit;

namespace VAL.Tests.Security
{
    public sealed class WebCommandRegistryTests
    {
        [Fact]
        public void IsAllowed_ReturnsTrueForKnownType()
        {
            var result = WebCommandRegistry.IsAllowed(WebMessageTypes.Command);

            Assert.True(result);
        }

        [Fact]
        public void IsAllowed_ReturnsFalseForUnknownType()
        {
            var result = WebCommandRegistry.IsAllowed("unknown.command");

            Assert.False(result);
        }

        [Fact]
        public void IsAllowed_ReturnsFalseForUnknownContinuumType()
        {
            var result = WebCommandRegistry.IsAllowed("continuum.custom.command");

            Assert.False(result);
        }
    }
}
