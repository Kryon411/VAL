using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

using VAL.App.Host.Services;

namespace VAL.App;

public partial class NavigationRecoveryOverlay : Window
{
    public event EventHandler? BrowserFocusRequested;
    internal bool OwnerShutdownAllowed { get; set; }

    private void NavigationRecoveryButton_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (!NativeControlKeyboardNavigation.IsReturnToPage(key, Keyboard.Modifiers)) return;
        e.Handled = true;
        if (!e.IsRepeat) BrowserFocusRequested?.Invoke(this, EventArgs.Empty);
    }

    public NavigationRecoveryOverlay(MainWindowViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!OwnerShutdownAllowed && Owner is { } owner)
        {
            e.Cancel = true;
            // Alt+F4 on the pill must use the main window's busy-exit guard, not remove recovery alone.
            _ = Dispatcher.InvokeAsync(owner.Close);
        }

        base.OnClosing(e);
    }
}
