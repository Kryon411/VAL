using System;

using VAL.Host.Services;

using Xunit;

namespace VAL.Tests.Startup;

public sealed class ValidationSessionSettingsTests
{
    [Fact]
    public void NormalLaunchDoesNotEnableValidation() => Assert.False(ValidationSessionSettings.FromArgs(null).Enabled);

    [Fact]
    public void InteractiveSessionDoesNotEnableAutomaticSmoke()
    {
        var args = new[] { "--validation-session", "--validation-data-root", "isolated" };
        Assert.True(ValidationSessionSettings.FromArgs(args).Enabled);
        Assert.Equal("isolated", ValidationSessionSettings.FromArgs(args).DataRoot);
        Assert.False(SmokeTestSettings.FromArgs(args).Enabled);
    }

    [Theory]
    [InlineData("--validation-data-root=path")]
    [InlineData("--validation-data-root=")]
    [InlineData("--validation-data-root")]
    public void InvalidRootArgumentsAreRejected(string argument) =>
        Assert.Throws<ArgumentException>(() => ValidationSessionSettings.FromArgs(new[] { argument }));
}
