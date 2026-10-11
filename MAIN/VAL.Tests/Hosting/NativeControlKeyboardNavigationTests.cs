using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

using VAL.App;
using VAL.App.Host.Services;
using VAL.App.ViewModels;
using VAL.Host;
using VAL.Host.Startup;
using VAL.Tests.Shared;

using Xunit;

namespace VAL.Tests.Hosting;

public sealed class NativeControlKeyboardNavigationTests
{
    [Theory]
    [InlineData(Key.F6, ModifierKeys.None, true, true)]
    [InlineData(Key.F6, ModifierKeys.Shift, true, true)]
    [InlineData(Key.F6, ModifierKeys.Control, false, false)]
    [InlineData(Key.F6, ModifierKeys.Alt, false, false)]
    [InlineData(Key.F6, ModifierKeys.Control | ModifierKeys.Shift, false, false)]
    [InlineData(Key.Tab, ModifierKeys.None, false, true)]
    [InlineData(Key.Tab, ModifierKeys.Shift, false, true)]
    [InlineData(Key.Tab, ModifierKeys.Control, false, false)]
    [InlineData(Key.Escape, ModifierKeys.None, false, true)]
    [InlineData(Key.Escape, ModifierKeys.Alt, false, false)]
    [InlineData(Key.Enter, ModifierKeys.None, false, false)]
    [InlineData(Key.Space, ModifierKeys.None, false, false)]
    [InlineData(Key.Left, ModifierKeys.Alt, false, false)]
    public void FocusKeysDoNotInterceptActivationMovementOrOtherModifiers(
        Key key, ModifierKeys modifiers, bool cycle, bool returnToPage)
    {
        Assert.Equal(cycle, NativeControlKeyboardNavigation.IsFocusCycle(key, modifiers));
        Assert.Equal(returnToPage, NativeControlKeyboardNavigation.IsReturnToPage(key, modifiers));
    }

    [Fact]
    public async Task HiddenNativeControlsCannotStealFocusAndLauncherAdvertisesItsShortcut()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var overlay = new ControlCentreOverlay(new NullLog());
                Assert.False(NativeControlKeyboardNavigation.TryFocus(overlay, overlay.LauncherButton));
                Assert.Equal("F6", AutomationProperties.GetAcceleratorKey(overlay.LauncherButton));
                Assert.Contains("Enter or Space", AutomationProperties.GetHelpText(overlay.LauncherButton));
                Assert.True(overlay.LauncherButton.Focusable);
                Assert.Equal("VAL - Control Centre", overlay.Title);
                overlay.OwnerShutdownAllowed = true;
                overlay.Close();
                using var operations = new OperationCoordinator();
                var recovery = new NativeNavigationRecoveryService(new TestWebViewRuntime { IsReady = true },
                    operations, new RecordingDesktopDialogs(), new NullLog());
                var model = new MainWindowViewModel(operations, null!, null!, null!, null!, null!, null!,
                    new StartupOptions(false, false, Array.Empty<string>()), new NullLog(), recovery);
                var pill = new NavigationRecoveryOverlay(model);
                Assert.False(NativeControlKeyboardNavigation.TryFocus(pill, pill.NavigationRecoveryButton));
                Assert.Equal("F6", AutomationProperties.GetAcceleratorKey(pill.NavigationRecoveryButton));
                Assert.Contains("without navigating", AutomationProperties.GetHelpText(pill.NavigationRecoveryButton));
                Assert.True(pill.NavigationRecoveryButton.Focusable);
                pill.OwnerShutdownAllowed = true;
                pill.Close();
                completion.SetResult();
            }
            catch (Exception ex) { completion.SetException(ex); }
        })
        { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private sealed class NullLog : ILog
    {
        public void Info(string category, string message) { }
        public void Warn(string category, string message) { }
        public void LogError(string category, string message) { }
        public void Verbose(string category, string message) { }
    }
}
