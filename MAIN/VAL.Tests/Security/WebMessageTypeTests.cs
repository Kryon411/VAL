using VAL.Contracts;
using VAL.Host.Security;
using VAL.Host.WebMessaging;

using Xunit;

namespace VAL.Tests.Security
{
    public sealed class WebMessageTypeTests
    {
        [Fact]
        public void OversizedEnvelopeIsRejectedBeforeParsing()
        {
            Assert.False(MessageEnvelope.TryParse(new string('x', MessageEnvelope.MaxJsonCharacters + 1), out _));
        }

        [Fact]
        public void TryGetTypeReturnsTypeWhenPresent()
        {
            var json = $"{{\"type\":\"{WebMessageTypes.Command}\",\"name\":\"{WebCommandNames.VoidCommandSetEnabled}\"}}";

            var result = WebMessageType.TryGetType(json, out var type);

            Assert.True(result);
            Assert.Equal(WebMessageTypes.Command, type);
        }

        [Fact]
        public void TryGetTypeReturnsFalseWhenMissingType()
        {
            var json = $"{{\"name\":\"{WebCommandNames.VoidCommandSetEnabled}\"}}";

            var result = WebMessageType.TryGetType(json, out var type);

            Assert.False(result);
            Assert.Equal(string.Empty, type);
        }

        [Fact]
        public void TryGetTypeReturnsFalseWhenTypeIsNotString()
        {
            var json = "{\"type\":42}";

            var result = WebMessageType.TryGetType(json, out var type);

            Assert.False(result);
            Assert.Equal(string.Empty, type);
        }

        [Fact]
        public void TryGetTypeReturnsFalseForMalformedJson()
        {
            var json = "{\"type\":";

            var result = WebMessageType.TryGetType(json, out var type);

            Assert.False(result);
            Assert.Equal(string.Empty, type);
        }
    }
}
