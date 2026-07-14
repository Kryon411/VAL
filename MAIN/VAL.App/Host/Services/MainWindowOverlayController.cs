using System;
using System.Windows;

namespace VAL.App.Host.Services
{
    public sealed class MainWindowOverlayController
    {
        private readonly MainWindowShellStateController _shellStateController;
        private readonly IControlCentreOverlayHost _overlayHost;

        public MainWindowOverlayController(
            MainWindowShellStateController shellStateController,
            IControlCentreOverlayHost overlayHost)
        {
            _shellStateController = shellStateController ?? throw new ArgumentNullException(nameof(shellStateController));
            _overlayHost = overlayHost ?? throw new ArgumentNullException(nameof(overlayHost));
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
            _overlayHost.PulseTopmost();
        }

        public void HandleDeactivated()
        {
            _overlayHost.ClearTopmost();
        }

        public void InitializeGeometry(Func<GeometryState> defaultGeometryFactory)
        {
            ArgumentNullException.ThrowIfNull(defaultGeometryFactory);

            var geometry = _shellStateController.ResolveControlCentreGeometry(defaultGeometryFactory);
            _overlayHost.ApplyGeometry(geometry);
            ClampAndPersistGeometry();
        }

        public void HandleGeometryChanged()
        {
            ClampAndPersistGeometry();
        }

        public void HandleOwnerStateChanged(WindowState ownerState)
        {
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
            if (!_shellStateController.IsLayoutModeEnabled)
            {
                ClampAndPersistGeometry();
            }
        }

        public bool HandleNavigationCompleted(WindowState ownerState, bool isActive)
        {
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

        private void ClampAndPersistGeometry()
        {
            if (_overlayHost.TryClampToVirtualScreen(out var geometry))
            {
                _shellStateController.UpdateControlCentreGeometry(geometry);
            }
        }
    }
}
