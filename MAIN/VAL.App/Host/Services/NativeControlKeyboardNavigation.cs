using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace VAL.App.Host.Services;

internal static class NativeControlKeyboardNavigation
{
    public static bool IsFocusCycle(Key key, ModifierKeys modifiers) =>
        key == Key.F6 && modifiers is ModifierKeys.None or ModifierKeys.Shift;

    public static bool IsReturnToPage(Key key, ModifierKeys modifiers) =>
        IsFocusCycle(key, modifiers) ||
        (key == Key.Tab && modifiers is ModifierKeys.None or ModifierKeys.Shift) ||
        (key == Key.Escape && modifiers == ModifierKeys.None);

    public static bool TryFocus(Window window, Control control)
    {
        if (!window.IsVisible || !window.IsEnabled || window.Owner is not { IsVisible: true } owner ||
            owner.WindowState == WindowState.Minimized || !control.IsVisible || !control.IsEnabled || !control.Focusable)
            return false;

        window.Activate();
        Keyboard.Focus(control);
        return control.IsKeyboardFocused;
    }
}
