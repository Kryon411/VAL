using System;
using System.Windows;

using VAL.Host.Services;

namespace VAL.App.Host.Services;

public sealed class NavigationRecoveryOverlayHost
{
    private readonly IWindowFactory<NavigationRecoveryOverlay> _windowFactory;
    private readonly NativeNavigationRecoveryService _navigationRecovery;
    private NavigationRecoveryOverlay? _overlay;
    private Window? _owner;

    public NavigationRecoveryOverlayHost(
        IWindowFactory<NavigationRecoveryOverlay> windowFactory,
        NativeNavigationRecoveryService navigationRecovery)
    {
        _windowFactory = windowFactory ?? throw new ArgumentNullException(nameof(windowFactory));
        _navigationRecovery = navigationRecovery ?? throw new ArgumentNullException(nameof(navigationRecovery));
    }

    internal NavigationRecoveryOverlay? Overlay => _overlay;
    public event EventHandler? BrowserFocusRequested;

    public bool TryFocus() => _navigationRecovery.IsAvailable && _overlay != null &&
        NativeControlKeyboardNavigation.TryFocus(_overlay, _overlay.NavigationRecoveryButton);

    public void Attach(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (_overlay != null) return;

        _owner = owner;
        _overlay = _windowFactory.Create();
        _overlay.Owner = owner;
        _overlay.Closed += Overlay_Closed;
        _overlay.BrowserFocusRequested += Overlay_BrowserFocusRequested;
    }

    public void Refresh(Rect viewport)
    {
        if (_overlay == null || _owner == null) return;
        if (!_navigationRecovery.IsAvailable || _owner.WindowState == WindowState.Minimized || viewport.IsEmpty)
        {
            _overlay.Hide();
            return;
        }

        // An owned native window stays above WebView2's HWND without changing the provider page.
        _overlay.MaxWidth = Math.Max(32, viewport.Width - 24);
        _overlay.NavigationRecoveryButton.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = _overlay.NavigationRecoveryButton.DesiredSize;
        size.Width = Math.Min(size.Width, _overlay.MaxWidth);
        var position = CalculatePosition(viewport, size);
        _overlay.Left = position.X;
        _overlay.Top = position.Y;
        if (!_overlay.IsVisible) _overlay.Show();
    }

    public void Close()
    {
        var overlay = _overlay;
        if (overlay == null) return;
        overlay.Closed -= Overlay_Closed;
        overlay.BrowserFocusRequested -= Overlay_BrowserFocusRequested;
        _overlay = null;
        _owner = null;
        overlay.OwnerShutdownAllowed = true;
        overlay.Close();
    }

    public void PrepareOwnerClose(bool cancelled)
    {
        if (_overlay != null) _overlay.OwnerShutdownAllowed = !cancelled;
    }

    internal static Point CalculatePosition(Rect viewport, Size size)
    {
        return new Point(
            Math.Clamp(viewport.Left + 12, viewport.Left, Math.Max(viewport.Left, viewport.Right - size.Width - 12)),
            Math.Clamp(viewport.Top + 12, viewport.Top, Math.Max(viewport.Top, viewport.Bottom - size.Height - 12)));
    }

    private void Overlay_Closed(object? sender, EventArgs e)
    {
        if (sender is NavigationRecoveryOverlay overlay)
        {
            overlay.Closed -= Overlay_Closed;
            overlay.BrowserFocusRequested -= Overlay_BrowserFocusRequested;
        }
        _overlay = null;
        _owner = null;
    }

    private void Overlay_BrowserFocusRequested(object? sender, EventArgs e) =>
        BrowserFocusRequested?.Invoke(this, EventArgs.Empty);
}
