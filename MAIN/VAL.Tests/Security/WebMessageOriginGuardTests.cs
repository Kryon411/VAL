using System;
using VAL.Host.Security;
using Xunit;

namespace VAL.Tests.Security
{
    public sealed class WebMessageOriginGuardTests
    {
        [Theory]
        [InlineData("https://chatgpt.com/")]
        [InlineData("https://chat.openai.com/c/abc")]
        public void TryIsAllowed_AllowsChatGptHosts(string source)
        {
            var result = WebMessageOriginGuard.TryIsAllowed(source, out var uri);

            Assert.True(result);
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
        public void TryIsAllowed_RejectsInvalidOrigins(string source)
        {
            var result = WebMessageOriginGuard.TryIsAllowed(source, out var uri);

            Assert.False(result);
            Assert.Null(uri);
        }
    }
}
