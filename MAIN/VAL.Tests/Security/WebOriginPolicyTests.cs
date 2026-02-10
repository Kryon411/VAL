using System;
using VAL.Host.Security;
using Xunit;

namespace VAL.Tests.Security
{
    public sealed class WebOriginPolicyTests
    {
        [Theory]
        [InlineData("https://chatgpt.com/")]
        [InlineData("https://chat.openai.com/c/uuid")]
        public void TryIsBridgeAllowedAllowsChatGptHosts(string source)
        {
            var result = WebOriginPolicy.TryIsBridgeAllowed(source, out var uri);

            Assert.True(result);
            Assert.NotNull(uri);
            Assert.True(Uri.TryCreate(source, UriKind.Absolute, out var expected));
            Assert.Equal(expected, uri);
        }

        [Theory]
        [InlineData("https://auth.openai.com/")]
        [InlineData("https://accounts.google.com/")]
        [InlineData("http://chatgpt.com/")]
        [InlineData("file:///C:/x")]
        public void TryIsBridgeAllowedRejectsNonChatGptHosts(string source)
        {
            var result = WebOriginPolicy.TryIsBridgeAllowed(source, out var uri);

            Assert.False(result);
            Assert.Null(uri);
        }

        [Theory]
        [InlineData("https://chatgpt.com/")]
        [InlineData("https://auth.openai.com/")]
        [InlineData("https://accounts.google.com/")]
        public void TryIsNavigationAllowedAllowsLoginHosts(string source)
        {
            var result = WebOriginPolicy.TryIsNavigationAllowed(source, out var uri);

            Assert.True(result);
            Assert.NotNull(uri);
        }

        [Theory]
        [InlineData("http://chatgpt.com/")]
        [InlineData("file:///C:/x")]
        [InlineData("edge://settings")]
        [InlineData("about:blank")]
        [InlineData("not a uri")]
        public void TryIsNavigationAllowedRejectsUnsafeUris(string source)
        {
            var result = WebOriginPolicy.TryIsNavigationAllowed(source, out var uri);

            Assert.False(result);
            Assert.Null(uri);
        }
    }
}
