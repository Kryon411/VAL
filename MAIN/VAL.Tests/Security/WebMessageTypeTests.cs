using VAL.Contracts;
using VAL.Host.Security;
using Xunit;

namespace VAL.Tests.Security
{
    public sealed class WebMessageTypeTests
    {
        [Fact]
        public void TryGetType_ReturnsTypeWhenPresent()
        {
            var json = $"{{\"type\":\"{WebMessageTypes.Command}\",\"name\":\"{WebCommandNames.VoidCommandSetEnabled}\"}}";

            var result = WebMessageType.TryGetType(json, out var type);

            Assert.True(result);
            Assert.Equal(WebMessageTypes.Command, type);
        }

        [Fact]
        public void TryGetType_ReturnsFalseWhenMissingType()
        {
            var json = $"{{\"name\":\"{WebCommandNames.VoidCommandSetEnabled}\"}}";

            var result = WebMessageType.TryGetType(json, out var type);

            Assert.False(result);
            Assert.Equal(string.Empty, type);
        }

        [Fact]
        public void TryGetType_ReturnsFalseWhenTypeIsNotString()
        {
            var json = "{\"type\":42}";

            var result = WebMessageType.TryGetType(json, out var type);

            Assert.False(result);
            Assert.Equal(string.Empty, type);
        }

        [Fact]
        public void TryGetType_ReturnsFalseForMalformedJson()
        {
            var json = "{\"type\":";

            var result = WebMessageType.TryGetType(json, out var type);

            Assert.False(result);
            Assert.Equal(string.Empty, type);
        }
    }
}
