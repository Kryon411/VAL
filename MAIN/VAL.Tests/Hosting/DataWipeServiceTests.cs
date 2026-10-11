using System.IO;

using VAL.App.Host.Services;
using VAL.Tests.Shared;

using Xunit;

namespace VAL.Tests.Hosting;

public sealed class DataWipeServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CleanupSafetyChecksApplyOnlyWhenARequestExists(bool pending)
    {
        using var paths = new TemporaryAppPaths();
        var service = new DataWipeService(paths, new NullTestLog());
        if (pending) service.ScheduleWipe();
        paths.ProductRoot = paths.DataRoot;

        var result = service.CompletePendingWipe();

        if (pending)
        {
            Assert.False(result!.Success);
            Assert.True(File.Exists(service.PendingPath));
        }
        else Assert.Null(result);
        Assert.Throws<System.InvalidOperationException>(service.ScheduleWipe);
    }

    [Fact]
    public void InvalidPendingPathCannotBeMistakenForNoRequest()
    {
        using var paths = new TemporaryAppPaths();
        var service = new DataWipeService(paths, new NullTestLog());
        Directory.CreateDirectory(service.PendingPath);

        Assert.False(service.CompletePendingWipe()!.Success);
        Assert.True(Directory.Exists(service.PendingPath));
    }

    [Theory]
    [InlineData("data-root")]
    [InlineData("outside")]
    [InlineData("program")]
    public void WipeRefusesBroadOrUnmanagedConfiguredTargets(string target)
    {
        using var paths = new TemporaryAppPaths();
        paths.LogsRoot = target switch
        {
            "data-root" => paths.DataRoot,
            "program" => paths.ProductRoot,
            _ => Path.Combine(paths.Root, "External"),
        };
        Directory.CreateDirectory(paths.LogsRoot);
        var sentinel = Path.Combine(paths.LogsRoot, "keep.txt");
        File.WriteAllText(sentinel, "unrelated data");
        var service = new DataWipeService(paths, new NullTestLog());

        Assert.Throws<System.InvalidOperationException>(service.ScheduleWipe);
        Assert.True(File.Exists(sentinel));
    }

    [Fact]
    public void WipeIsDeferredAndDeletesOnlyManagedSubdirectories()
    {
        using var paths = new TemporaryAppPaths();
        Directory.CreateDirectory(paths.MemoryChatsRoot);
        File.WriteAllText(Path.Combine(paths.MemoryChatsRoot, "archive.txt"), "private");
        File.WriteAllText(Path.Combine(paths.DataRoot, "settings.json"), "preferences");
        var service = new DataWipeService(paths, new NullTestLog());
        service.ScheduleWipe();
        Assert.True(Directory.Exists(paths.MemoryChatsRoot));
        Assert.True(File.Exists(service.PendingPath));

        var result = new DataWipeService(paths, new NullTestLog()).CompletePendingWipe();

        Assert.True(result!.Success);
        Assert.False(Directory.Exists(paths.MemoryChatsRoot));
        Assert.True(File.Exists(Path.Combine(paths.DataRoot, "settings.json")));
        Assert.False(File.Exists(service.PendingPath));
        Assert.Null(service.CompletePendingWipe());
    }

    [Fact]
    public void LockedDataKeepsPendingRequestForRetry()
    {
        using var paths = new TemporaryAppPaths();
        Directory.CreateDirectory(paths.MemoryChatsRoot);
        var archive = Path.Combine(paths.MemoryChatsRoot, "archive.txt");
        File.WriteAllText(archive, "private");
        var service = new DataWipeService(paths, new NullTestLog());
        service.ScheduleWipe();
        using (var locked = File.Open(archive, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.False(service.CompletePendingWipe()!.Success);
            Assert.True(File.Exists(service.PendingPath));
        }
        Assert.True(service.CompletePendingWipe()!.Success);
        Assert.False(File.Exists(archive));
    }

    [Fact]
    public void ChangedConfigurationAndMalformedRequestFailClosed()
    {
        using var paths = new TemporaryAppPaths();
        var service = new DataWipeService(paths, new NullTestLog());
        service.ScheduleWipe();
        var original = paths.LogsRoot;
        paths.LogsRoot = Path.Combine(paths.DataRoot, "OtherLogs");
        Assert.False(service.CompletePendingWipe()!.Success);
        paths.LogsRoot = original;
        File.WriteAllText(service.PendingPath, "{broken");
        Assert.False(service.CompletePendingWipe()!.Success);
        Assert.True(File.Exists(service.PendingPath));
    }
}
