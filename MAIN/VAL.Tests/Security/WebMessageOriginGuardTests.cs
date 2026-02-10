using System;
using VAL.Host.Security;
using Xunit;

namespace VAL.Tests.Security
{
    public sealed class WebMessageOriginGuardTests
    {
        private const string ExpectedNonce = "nonce-value";

        [Theory]
        [InlineData("https://chatgpt.com/")]
        [InlineData("https://chat.openai.com/c/abc")]
        public void TryIsAllowedAllowsChatGptHosts(string source)
        {
            var result = WebMessageOriginGuard.TryIsAllowed(source, ExpectedNonce, ExpectedNonce, out var uri, out var reason);

            Assert.True(result);
            Assert.Null(reason);
            Assert.NotNull(uri);
            Assert.True(Uri.TryCreate(source, UriKind.Absolute, out var expected));
            Assert.Equal(expected, uri);
        }

        [Theory]
        [InlineData("http://chatgpt.com/")]
        [InlineData("https://evil.com/")]
        [InlineData("file:///C:/test.html")]
        [InlineData("")]
        [InlineData("not a uri")]
        public void TryIsAllowedRejectsInvalidOrigins(string source)
        {
            var result = WebMessageOriginGuard.TryIsAllowed(source, ExpectedNonce, ExpectedNonce, out var uri, out var reason);

            Assert.False(result);
            Assert.Null(uri);
            Assert.Equal("origin_not_allowlisted", reason);
        }

        [Fact]
        public void TryIsAllowedRejectsMissingNonce()
        {
            var result = WebMessageOriginGuard.TryIsAllowed("https://chatgpt.com/", null, ExpectedNonce, out var uri, out var reason);

            Assert.False(result);
            Assert.Null(uri);
            Assert.Equal("nonce_missing", reason);
        }

        [Fact]
        public void TryIsAllowedRejectsMismatchedNonce()
        {
            var result = WebMessageOriginGuard.TryIsAllowed("https://chatgpt.com/", "wrong", ExpectedNonce, out var uri, out var reason);

            Assert.False(result);
            Assert.Null(uri);
            Assert.Equal("nonce_mismatch", reason);
        }
    }
}
