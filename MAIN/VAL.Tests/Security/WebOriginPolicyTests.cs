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
        [InlineData("https://chatgpt.com:8443/")]
        [InlineData("https://user:password@chatgpt.com/")]
        public void TryIsBridgeAllowedRejectsNonChatGptHosts(string source)
        {
            var result = WebOriginPolicy.TryIsBridgeAllowed(source, out var uri);

            Assert.False(result);
            Assert.Null(uri);
        }

        [Theory]
        [InlineData("https://chatgpt.com/auth/login?returnTo=%2Fc%2Fprivate")]
        [InlineData("https://chat.openai.com/api/auth/session")]
        [InlineData("https://chatgpt.com/login")]
        [InlineData("https://chatgpt.com/logout/")]
        [InlineData("https://chatgpt.com/signin")]
        [InlineData("https://chatgpt.com/signup")]
        [InlineData("https://chatgpt.com/sign-in")]
        [InlineData("https://chatgpt.com/sign-up")]
        [InlineData("https://chatgpt.com/oauth/callback")]
        [InlineData("https://chatgpt.com/oauth2/callback")]
        [InlineData("https://chatgpt.com/AUTH/login")]
        [InlineData("https://chatgpt.com/%61uth/login")]
        [InlineData("https://chatgpt.com/auth%2Flogin")]
        [InlineData("https://chatgpt.com/auth%5Clogin")]
        [InlineData("https://chatgpt.com/%2561uth/login")]
        public void AuthenticationRoutesAllowNavigationButNeverTheBridge(string source)
        {
            Assert.True(WebOriginPolicy.TryIsNavigationAllowed(source, out _));
            Assert.False(WebOriginPolicy.TryIsBridgeAllowed(source, out var uri));
            Assert.Null(uri);
        }

        [Theory]
        [InlineData("https://chatgpt.com/authentication")]
        [InlineData("https://chatgpt.com/c/auth")]
        [InlineData("https://chatgpt.com/g/project-login")]
        [InlineData("https://chatgpt.com/?next=/auth/login")]
        public void AuthenticationRouteMatchingUsesOnlyWholePathSegments(string source)
        {
            Assert.True(WebOriginPolicy.TryIsBridgeAllowed(source, out _));
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
        [InlineData("https://openai.com/")]
        [InlineData("https://appleid.apple.com/")]
        [InlineData("https://login.microsoftonline.com/")]
        public void TryIsNavigationAllowedIncludesCanonicalLoginOrigins(string source)
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
