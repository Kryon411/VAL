using System;
using System.Windows;

using VAL.Host.Services;

namespace VAL.App.Host.Services
{
    public sealed class MainWindowOverlayController
    {
        private readonly MainWindowShellStateController _shellStateController;
        private readonly IControlCentreOverlayHost _overlayHost;
        private readonly IWebViewRuntime _webViewRuntime;
        private Func<GeometryState>? _defaultGeometryFactory;
        private Func<Rect>? _viewportBoundsFactory;
        private Rect _lastViewportBounds = Rect.Empty;

        public MainWindowOverlayController(
            MainWindowShellStateController shellStateController,
            IControlCentreOverlayHost overlayHost,
            IWebViewRuntime webViewRuntime)
        {
            _shellStateController = shellStateController ?? throw new ArgumentNullException(nameof(shellStateController));
            _overlayHost = overlayHost ?? throw new ArgumentNullException(nameof(overlayHost));
            _webViewRuntime = webViewRuntime ?? throw new ArgumentNullException(nameof(webViewRuntime));
        }

        public void Attach(Window owner)
        {
            _overlayHost.EnsureAttached(owner);
        }

        public void Close()
        {
            _overlayHost.Close();
        }

        public void ApplyLayoutMode()
        {
            _overlayHost.ApplyLayoutMode(_shellStateController.IsLayoutModeEnabled);
        }

        public void HandleActivated()
        {
            if (!EnsureBridgeReady()) return;

            _overlayHost.PulseTopmost();
        }

        public void HandleDeactivated()
        {
            _overlayHost.ClearTopmost();
        }

        public void InitializeGeometry(Func<GeometryState> defaultGeometryFactory, Func<Rect>? viewportBoundsFactory = null)
        {
            ArgumentNullException.ThrowIfNull(defaultGeometryFactory);
            _defaultGeometryFactory = defaultGeometryFactory;
            _viewportBoundsFactory = viewportBoundsFactory;

            var geometry = _shellStateController.ResolveControlCentreGeometry(defaultGeometryFactory);
            _overlayHost.ApplyGeometry(geometry);
            ClampAndPersistGeometry();
        }

        public void HandleGeometryChanged()
        {
            if (!EnsureBridgeReady()) return;
            ClampAndPersistGeometry(userChanged: true);
        }

        public void HandleOwnerStateChanged(WindowState ownerState)
        {
            if (!EnsureBridgeReady()) return;
            if (ownerState == WindowState.Minimized)
            {
                _overlayHost.Hide();
                return;
            }

            _overlayHost.ShowIfNeeded(ownerState);
            if (!_shellStateController.IsLayoutModeEnabled)
            {
                ClampAndPersistGeometry();
            }
        }

        public void HandleOwnerBoundsChanged()
        {
            if (!EnsureBridgeReady()) return;
            if (_overlayHost.IsInteracting) return;
            var bounds = _viewportBoundsFactory?.Invoke() ?? Rect.Empty;
            if (!_shellStateController.IsControlCentreCustom && _defaultGeometryFactory != null)
                _overlayHost.ApplyGeometry(_defaultGeometryFactory());
            else if (!bounds.IsEmpty && !_lastViewportBounds.IsEmpty)
            {
                var geometry = _shellStateController.CreateSnapshot().ControlCentre;
                geometry.X += bounds.X - _lastViewportBounds.X;
                geometry.Y += bounds.Y - _lastViewportBounds.Y;
                _overlayHost.ApplyGeometry(geometry);
            }
            if (!_shellStateController.IsLayoutModeEnabled)
            {
                ClampAndPersistGeometry();
            }
        }

        public bool HandleNavigationCompleted(WindowState ownerState, bool isActive)
        {
            if (!EnsureBridgeReady()) return false;

            var refreshPlan = ShellNavigationRefreshPlan.For(ownerState, isActive);
            if (refreshPlan.ShowOverlay)
            {
                _overlayHost.ShowIfNeeded(ownerState);
            }

            if (refreshPlan.PulseTopmost)
            {
                _overlayHost.PulseTopmost();
            }

            return refreshPlan.RequestDockStateSync;
        }

        public void HandleNavigationStateChanged(WindowState ownerState)
        {
            HandleOwnerStateChanged(ownerState);
        }

        private bool EnsureBridgeReady()
        {
            if (_webViewRuntime.IsBridgeReady) return true;
            _overlayHost.Hide();
            return false;
        }

        private void ClampAndPersistGeometry(bool userChanged = false)
        {
            var bounds = _viewportBoundsFactory?.Invoke() ?? Rect.Empty;
            if (_overlayHost.TryClampToVirtualScreen(out var geometry, bounds.IsEmpty ? null : bounds))
            {
                if (!bounds.IsEmpty && bounds.Width >= 34 && bounds.Height >= 34)
                {
                    geometry = ControlCentreOverlayHost.ClampGeometry(geometry, bounds, 34, 34, 64, 64);
                    _overlayHost.ApplyGeometry(geometry);
                    _lastViewportBounds = bounds;
                }
                _shellStateController.UpdateControlCentreGeometry(geometry, userChanged);
            }
        }
    }
}
