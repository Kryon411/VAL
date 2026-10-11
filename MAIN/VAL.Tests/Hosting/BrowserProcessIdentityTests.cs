using System;
using System.Diagnostics;

using VAL.App.Host.Services;

using Xunit;

namespace VAL.Tests.Hosting;

public sealed class BrowserProcessIdentityTests
{
    [Fact]
    public void LiveMatchingProcessBlocksCleanupWithoutBeingTerminated()
    {
        using var process = Process.GetCurrentProcess();
        var identity = new BrowserProcessIdentity(process.Id, process.StartTime.ToUniversalTime().Ticks);
        Assert.False(identity.WaitForExit(TimeSpan.Zero));
        Assert.False(process.HasExited);
    }

    [Fact]
    public void ReusedPidAndExitedBrowserDoNotBlockCleanup()
    {
        Assert.True(new BrowserProcessIdentity(Environment.ProcessId, 1).WaitForExit(TimeSpan.Zero));
        Assert.True(new BrowserProcessIdentity(int.MaxValue, 1).WaitForExit(TimeSpan.Zero));
        Assert.Null(BrowserProcessIdentity.Capture(null));
    }
}
