using System;
using System.Collections.Generic;

using VAL.App.Host.Services;
using VAL.App.ViewModels;
using VAL.Host;
using VAL.Host.Startup;
using VAL.Tests.Shared;

using Xunit;

namespace VAL.Tests.Hosting;

public sealed class NativeNavigationRecoveryTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void RecoveryIsAvailableOnlyWithInitializedBrowserAndDisarmedBridge(bool ready, bool bridgeReady, bool available)
    {
        using var operations = new OperationCoordinator();
        var runtime = new TestWebViewRuntime { IsReady = ready, IsBridgeReady = bridgeReady };
        var service = new NativeNavigationRecoveryService(runtime, operations, new RecordingDesktopDialogs(), new RecordingLog());

        Assert.Equal(available, service.IsAvailable);
        Assert.Equal(available, service.TryReturnToChatGpt());
        Assert.Equal(available ? 1 : 0, runtime.NavigationRequests.Count);
        Assert.Equal(0, runtime.ScriptCalls);
        Assert.Equal(0, runtime.PostedMessages);
        Assert.Equal(bridgeReady, runtime.IsBridgeReady);
    }

    [Fact]
    public void ReturnUsesFixedHomeWithoutRearmingBridgeOrReopeningRememberedChat()
    {
        using var operations = new OperationCoordinator();
        var runtime = new TestWebViewRuntime
        {
            IsReady = true,
            LastChatUri = new Uri("https://chatgpt.com/c/remembered-chat"),
        };
        var dialogs = new RecordingDesktopDialogs();
        var service = new NativeNavigationRecoveryService(runtime, operations, dialogs, new RecordingLog());

        Assert.True(service.TryReturnToChatGpt());

        Assert.Equal("https://chatgpt.com/", Assert.Single(runtime.NavigationRequests));
        Assert.False(runtime.IsBridgeReady);
        Assert.Equal(0, runtime.ScriptCalls);
        Assert.Equal(0, runtime.PostedMessages);
        Assert.Empty(dialogs.Errors);
    }

    [Fact]
    public void BusyReturnDoesNotNavigateOrCancelTheActiveOperation()
    {
        using var operations = new OperationCoordinator();
        Assert.True(operations.TryBegin(GuardedOperationKind.Chronicle, out var token));
        var runtime = new TestWebViewRuntime { IsReady = true };
        var dialogs = new RecordingDesktopDialogs();
        var service = new NativeNavigationRecoveryService(runtime, operations, dialogs, new RecordingLog());

        Assert.False(service.TryReturnToChatGpt());

        Assert.Empty(runtime.NavigationRequests);
        Assert.True(operations.IsBusy);
        Assert.False(token.IsCancellationRequested);
        Assert.Contains("Finish or cancel", Assert.Single(dialogs.Errors).Message);
    }

    [Fact]
    public void NavigationFailureDoesNotEchoPageDetailsInLogsOrNativeDialog()
    {
        using var operations = new OperationCoordinator();
        var runtime = new TestWebViewRuntime
        {
            IsReady = true,
            NavigationFailure = new InvalidOperationException("https://accounts.google.com/?token=secret-fixture"),
        };
        var dialogs = new RecordingDesktopDialogs();
        var log = new RecordingLog();
        var service = new NativeNavigationRecoveryService(runtime, operations, dialogs, log);

        Assert.False(service.TryReturnToChatGpt());

        Assert.Empty(runtime.NavigationRequests);
        Assert.DoesNotContain("secret-fixture", Assert.Single(log.Warnings));
        Assert.DoesNotContain("secret-fixture", Assert.Single(dialogs.Errors).Message);
    }

    [Fact]
    public void VisibilityAndCommandRefreshWhenBridgeDisarmsAndRecovers()
    {
        using var operations = new OperationCoordinator();
        var runtime = new TestWebViewRuntime();
        var service = new NativeNavigationRecoveryService(runtime, operations, new RecordingDesktopDialogs(), new RecordingLog());
        var model = new MainWindowViewModel(operations, null!, null!, null!, null!, null!, null!,
            new StartupOptions(false, false, Array.Empty<string>()), new RecordingLog(), service);
        var changedProperties = new List<string?>();
        var commandChanges = 0;
        model.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);
        model.ReturnToChatGptCommand.CanExecuteChanged += (_, _) => commandChanges++;

        model.RefreshNavigationAvailability();
        Assert.False(model.IsNavigationRecoveryVisible);
        Assert.False(model.ReturnToChatGptCommand.CanExecute(null));
        runtime.IsReady = true;
        model.RefreshNavigationAvailability();
        model.RefreshNavigationAvailability();
        Assert.True(model.IsNavigationRecoveryVisible);
        Assert.True(model.ReturnToChatGptCommand.CanExecute(null));
        model.ReturnToChatGptCommand.Execute(null);
        Assert.Equal(NativeNavigationRecoveryService.ChatHomeUrl, Assert.Single(runtime.NavigationRequests));

        runtime.IsBridgeReady = true;
        model.RefreshNavigationAvailability();
        Assert.False(model.IsNavigationRecoveryVisible);
        Assert.False(model.ReturnToChatGptCommand.CanExecute(null));
        model.ReturnToChatGptCommand.Execute(null);
        Assert.Single(runtime.NavigationRequests);
        Assert.Equal(2, commandChanges);
        Assert.Equal(new[] { nameof(model.IsNavigationRecoveryVisible), nameof(model.IsNavigationRecoveryVisible) }, changedProperties);
    }

    private sealed class RecordingLog : ILog
    {
        public List<string> Warnings { get; } = [];
        public void Info(string category, string message) { }
        public void Warn(string category, string message) => Warnings.Add(message);
        public void LogError(string category, string message) { }
        public void Verbose(string category, string message) { }
    }
}
