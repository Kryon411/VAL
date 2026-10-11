using System;
using System.Windows;

namespace VAL.App.Host.Services
{
    public interface IControlCentreOverlayHost
    {
        bool IsInteracting { get; }
        event EventHandler? Clicked;
        event EventHandler? GeometryChanged;
        event EventHandler? LayoutToggleRequested;
        event EventHandler? BrowserFocusRequested;

        bool TryFocus();

        void EnsureAttached(Window owner);

        void Close();

        void ShowIfNeeded(WindowState ownerState);

        void Hide();

        void PulseTopmost();

        void ClearTopmost();

        void ApplyLayoutMode(bool enabled);

        void ApplyGeometry(GeometryState geometry);

        bool TryClampToVirtualScreen(out GeometryState geometry, Rect? viewportBounds = null);
    }
}
