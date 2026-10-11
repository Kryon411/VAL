using VAL.Host.Security;

using Xunit;

namespace VAL.Tests.Security;

public sealed class WebViewScriptPolicyTests
{
    [Fact]
    public void BrowserLogsDoNotContainCredentialsPathsOrTokens()
    {
        Assert.Equal("https://chatgpt.com", WebViewScriptPolicy.LogOrigin("https://user:password@chatgpt.com/c/private?token=secret#fragment"));
    }

    [Fact]
    public void ScriptsAndNonceAreRestrictedToTopLevelBridgeOrigins()
    {
        var guarded = WebViewScriptPolicy.Guard("window.test = true;");
        Assert.Contains("window !== window.top", guarded);
        Assert.Contains("window.location.origin", guarded);
        Assert.Contains("window.location.pathname", guarded);
        Assert.Contains("https://chatgpt.com", guarded);
        Assert.Contains("window.test = true;", guarded);
        foreach (var prefix in WebOriginPolicy.AuthenticationPathPrefixes)
            Assert.Contains('"' + prefix + '"', guarded);
        Assert.DoesNotContain("__VAL_AUTH_PATHS__", guarded);
        Assert.DoesNotContain("__VAL_SCRIPT__", guarded);
    }
}
