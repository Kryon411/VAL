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
        public void TryIsAllowed_AllowsChatGptOrigins(string source)
        {
            var allowed = WebMessageOriginGuard.TryIsAllowed(source, out var uri);

            Assert.True(allowed);
            Assert.NotNull(uri);
            Assert.True(uri!.IsAbsoluteUri);
        }

        [Theory]
        [InlineData("http://chatgpt.com/")]
        [InlineData("https://evil.com/")]
        [InlineData("file:///C:/test.html")]
        [InlineData("")]
        [InlineData("not a uri")]
        public void TryIsAllowed_RejectsNonAllowlistedOrigins(string source)
        {
            var allowed = WebMessageOriginGuard.TryIsAllowed(source, out var uri);

            Assert.False(allowed);
            Assert.Null(uri);
        }
    }
}
