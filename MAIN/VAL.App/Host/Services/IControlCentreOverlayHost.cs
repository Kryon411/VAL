using System;
using System.Windows;

namespace VAL.App.Host.Services
{
    public interface IControlCentreOverlayHost
    {
        event EventHandler? Clicked;
        event EventHandler? GeometryChanged;
        event EventHandler? LayoutToggleRequested;

        void EnsureAttached(Window owner);

        void Close();

        void ShowIfNeeded(WindowState ownerState);

        void Hide();

        void PulseTopmost();

        void ClearTopmost();

        void ApplyLayoutMode(bool enabled);

        void ApplyGeometry(GeometryState geometry);

        bool TryClampToVirtualScreen(out GeometryState geometry);
    }
}
