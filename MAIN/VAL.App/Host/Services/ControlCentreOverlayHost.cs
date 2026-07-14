using System;
using System.Windows;

using VAL.Host;

namespace VAL.App.Host.Services
{
    public sealed class ControlCentreOverlayHost : IControlCentreOverlayHost
    {
        private readonly IWindowFactory<ControlCentreOverlay> _windowFactory;
        private readonly ILog _log;
        private ControlCentreOverlay? _overlay;

        public ControlCentreOverlayHost(
            IWindowFactory<ControlCentreOverlay> windowFactory,
            ILog log)
        {
            _windowFactory = windowFactory ?? throw new ArgumentNullException(nameof(windowFactory));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public event EventHandler? Clicked;
        public event EventHandler? GeometryChanged;
        public event EventHandler? LayoutToggleRequested;

        public void EnsureAttached(Window owner)
        {
            ArgumentNullException.ThrowIfNull(owner);

            if (_overlay != null)
            {
                return;
            }

            _overlay = _windowFactory.Create();
            _overlay.Owner = owner;
            _overlay.Clicked += Overlay_Clicked;
            _overlay.GeometryChanged += Overlay_GeometryChanged;
            _overlay.LayoutToggleRequested += Overlay_LayoutToggleRequested;
            _overlay.Closed += Overlay_Closed;
        }

        public void Close()
        {
            if (_overlay == null)
            {
                return;
            }

            try
            {
                var overlay = _overlay;
                DetachOverlay(overlay);
                overlay.Close();
            }
            catch
            {
                _log.Warn(nameof(ControlCentreOverlayHost), "Failed to close Control Centre overlay window.");
            }
        }

        public void ShowIfNeeded(WindowState ownerState)
        {
            if (_overlay == null || ownerState == WindowState.Minimized || _overlay.IsVisible)
            {
                return;
            }

            _overlay.Show();
        }

        public void Hide()
        {
            _overlay?.Hide();
        }

        public void PulseTopmost()
        {
            if (_overlay == null)
            {
                return;
            }

            _overlay.Topmost = true;
            _overlay.Topmost = false;
        }

        public void ClearTopmost()
        {
            if (_overlay != null)
            {
                _overlay.Topmost = false;
            }
        }

        public void ApplyLayoutMode(bool enabled)
        {
            if (_overlay != null)
            {
                _overlay.LayoutModeEnabled = enabled;
            }
        }

        public void ApplyGeometry(GeometryState geometry)
        {
            if (_overlay == null)
            {
                return;
            }

            _overlay.Width = geometry.W;
            _overlay.Height = geometry.H;
            _overlay.Left = geometry.X;
            _overlay.Top = geometry.Y;
        }

        public bool TryClampToVirtualScreen(out GeometryState geometry)
        {
            geometry = default;
            if (_overlay == null)
            {
                return false;
            }

            geometry = new GeometryState(_overlay.Left, _overlay.Top, _overlay.Width, _overlay.Height);
            geometry = ClampGeometry(
                geometry,
                GetVirtualScreenBounds(),
                _overlay.MinWidth,
                _overlay.MinHeight,
                _overlay.MaxWidth,
                _overlay.MaxHeight);

            ApplyGeometry(geometry);
            return true;
        }

        internal static GeometryState ClampGeometry(
            GeometryState geometry,
            Rect bounds,
            double minWidth,
            double minHeight,
            double maxWidth,
            double maxHeight)
        {
            var effectiveMaxWidth = ResolveMaximum(maxWidth, geometry.W);
            var effectiveMaxHeight = ResolveMaximum(maxHeight, geometry.H);

            geometry.W = Math.Clamp(geometry.W, minWidth, effectiveMaxWidth);
            geometry.H = Math.Clamp(geometry.H, minHeight, effectiveMaxHeight);
            geometry.X = Math.Clamp(geometry.X, bounds.Left, Math.Max(bounds.Left, bounds.Right - geometry.W));
            geometry.Y = Math.Clamp(geometry.Y, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - geometry.H));
            return geometry;
        }

        private static double ResolveMaximum(double configuredMaximum, double fallback)
        {
            if (double.IsNaN(configuredMaximum) ||
                double.IsInfinity(configuredMaximum) ||
                configuredMaximum <= 0)
            {
                return Math.Max(fallback, 0);
            }

            return configuredMaximum;
        }

        private static Rect GetVirtualScreenBounds()
        {
            return new Rect(
                SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth,
                SystemParameters.VirtualScreenHeight);
        }

        private void Overlay_Closed(object? sender, EventArgs e)
        {
            if (sender is ControlCentreOverlay overlay)
            {
                DetachOverlay(overlay);
            }
        }

        private void Overlay_Clicked(object? sender, EventArgs e)
        {
            Clicked?.Invoke(this, EventArgs.Empty);
        }

        private void Overlay_GeometryChanged(object? sender, EventArgs e)
        {
            GeometryChanged?.Invoke(this, EventArgs.Empty);
        }

        private void Overlay_LayoutToggleRequested(object? sender, EventArgs e)
        {
            LayoutToggleRequested?.Invoke(this, EventArgs.Empty);
        }

        private void DetachOverlay(ControlCentreOverlay overlay)
        {
            overlay.Clicked -= Overlay_Clicked;
            overlay.GeometryChanged -= Overlay_GeometryChanged;
            overlay.LayoutToggleRequested -= Overlay_LayoutToggleRequested;
            overlay.Closed -= Overlay_Closed;

            if (ReferenceEquals(_overlay, overlay))
            {
                _overlay = null;
            }
        }
    }
}
