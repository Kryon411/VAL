using System;
using System.IO;

using VAL.App.Host.Services;
using VAL.Tests.Shared;

using Xunit;

namespace VAL.Tests.Hosting;

public sealed class PrivacySettingsServiceTests
{
    [Fact]
    public void InvalidSettingsPathCannotEnableDefaultCapture()
    {
        using var paths = new TemporaryAppPaths();
        Directory.CreateDirectory(Path.Combine(paths.DataRoot, "settings.json"));
        var logging = true;

        var service = new PrivacySettingsService(paths, enabled => logging = enabled, new NullTestLog());

        Assert.False(logging);
        Assert.False(service.GetSnapshot().ContinuumLoggingEnabled);
        Assert.False(service.GetSnapshot().PortalCaptureEnabled);
    }

    [Fact]
    public void DisableCapturePersistsBothSettingsWithOneNotification()
    {
        using var paths = new TemporaryAppPaths();
        var logging = true;
        var changes = 0;
        var service = new PrivacySettingsService(paths, enabled => logging = enabled, new NullTestLog());
        service.SettingsChanged += snapshot =>
        {
            changes++;
            Assert.False(snapshot.ContinuumLoggingEnabled);
            Assert.False(snapshot.PortalCaptureEnabled);
        };
        Assert.True(service.DisableCapture());
        Assert.False(logging);
        Assert.Equal(1, changes);
        var reloaded = new PrivacySettingsService(paths, _ => { }, new NullTestLog()).GetSnapshot();
        Assert.False(reloaded.ContinuumLoggingEnabled);
        Assert.False(reloaded.PortalCaptureEnabled);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("{}")]
    [InlineData("{\"version\":99,\"continuumLoggingEnabled\":true,\"portalCaptureEnabled\":true}")]
    public void DamagedOrUnsupportedSettingsDisableCapture(string json)
    {
        using var paths = new TemporaryAppPaths();
        File.WriteAllText(Path.Combine(paths.DataRoot, "settings.json"), json);
        var logging = true;
        var service = new PrivacySettingsService(paths, enabled => logging = enabled, new NullTestLog());

        Assert.False(logging);
        Assert.False(service.GetSnapshot().ContinuumLoggingEnabled);
        Assert.False(service.GetSnapshot().PortalCaptureEnabled);
    }

    [Fact]
    public void FailedSaveDoesNotClaimToHaveChangedSettings()
    {
        using var paths = new TemporaryAppPaths();
        var path = Path.Combine(paths.DataRoot, "settings.json");
        File.WriteAllText(path, "{\"version\":1,\"continuumLoggingEnabled\":false,\"portalCaptureEnabled\":false}");
        var logging = true;
        var service = new PrivacySettingsService(paths, enabled => logging = enabled, new NullTestLog());
        using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Throws<IOException>(() => service.UpdateContinuumLogging(true));
        }

        Assert.False(logging);
        Assert.False(service.GetSnapshot().ContinuumLoggingEnabled);
    }

    [Fact]
    public void FailingSubscriberCannotPreventLoggingSettingFromApplying()
    {
        using var paths = new TemporaryAppPaths();
        var logging = true;
        var notified = false;
        var service = new PrivacySettingsService(paths, enabled => logging = enabled, new NullTestLog());
        service.SettingsChanged += _ => throw new InvalidOperationException();
        service.SettingsChanged += _ => notified = true;

        Assert.True(service.UpdateContinuumLogging(false));

        Assert.False(logging);
        Assert.True(notified);
        Assert.False(new PrivacySettingsService(paths, _ => { }, new NullTestLog()).GetSnapshot().ContinuumLoggingEnabled);
    }
}
