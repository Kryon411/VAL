using VAL.App.Host.Services;
using VAL.App.ViewModels;
using VAL.Host;
using VAL.Host.Startup;
using VAL.Tests.Shared;

using Xunit;

namespace VAL.Tests.Hosting;

public sealed class MainWindowCloseGuardTests
{
    [Fact]
    public void DecliningExitDoesNotAuthorizeTheNextCloseAttempt()
    {
        using var operations = new OperationCoordinator();
        Assert.True(operations.TryBegin(GuardedOperationKind.Chronicle, out var token));
        var model = new MainWindowViewModel(operations, null!, null!, null!, null!, null!, null!,
            new StartupOptions(false, false, System.Array.Empty<string>()), new NullLog(),
            new NativeNavigationRecoveryService(new TestWebViewRuntime(), operations, new RecordingDesktopDialogs(), new NullLog()));

        Assert.True(model.ShouldCancelClose(() => false));
        Assert.True(model.ShouldCancelClose(() => false));
        Assert.False(token.IsCancellationRequested);
        Assert.False(model.ShouldCancelClose(() => true));
        Assert.True(token.IsCancellationRequested);
    }

    private sealed class NullLog : ILog
    {
        public void Info(string category, string message) { }
        public void Warn(string category, string message) { }
        public void LogError(string category, string message) { }
        public void Verbose(string category, string message) { }
    }
}
