using System;
using System.Windows;

using VAL.App.Host.Services;
using VAL.App.State;

using Xunit;

namespace VAL.Tests.Hosting
{
    public sealed class MainWindowOverlayControllerTests
    {
        [Fact]
        public void InitializeGeometryUsesDefaultFactoryAndPersistsClampedGeometry()
        {
            var stateController = CreateStateController();
            stateController.Load();
            var overlayHost = new FakeControlCentreOverlayHost
            {
                ClampedGeometry = new GeometryState(120, 140, 40, 40)
            };
            var controller = new MainWindowOverlayController(stateController, overlayHost);
            var factoryCalls = 0;

            controller.InitializeGeometry(() =>
            {
                factoryCalls++;
                return new GeometryState(100, 110, 40, 40);
            });

            var snapshot = stateController.CreateSnapshot();

            Assert.Equal(1, factoryCalls);
            Assert.Equal(new GeometryState(100, 110, 40, 40), overlayHost.LastAppliedGeometry);
            Assert.Equal(120, snapshot.ControlCentre.X);
            Assert.Equal(140, snapshot.ControlCentre.Y);
            Assert.Equal(40, snapshot.ControlCentre.W);
            Assert.Equal(40, snapshot.ControlCentre.H);
            Assert.Equal(1, overlayHost.ClampCalls);
        }

        [Fact]
        public void HandleOwnerStateChangedHidesOverlayWhenMinimized()
        {
            var overlayHost = new FakeControlCentreOverlayHost();
            var controller = new MainWindowOverlayController(CreateLoadedStateController(), overlayHost);

            controller.HandleOwnerStateChanged(WindowState.Minimized);

            Assert.Equal(1, overlayHost.HideCalls);
            Assert.Equal(0, overlayHost.ShowCalls);
        }

        [Fact]
        public void HandleOwnerBoundsChangedSkipsClampWhenLayoutModeIsEnabled()
        {
            var stateController = CreateLoadedStateController();
            stateController.ToggleLayoutMode();
            var overlayHost = new FakeControlCentreOverlayHost();
            var controller = new MainWindowOverlayController(stateController, overlayHost);

            controller.HandleOwnerBoundsChanged();

            Assert.Equal(0, overlayHost.ClampCalls);
        }

        [Fact]
        public void HandleNavigationCompletedShowsPulsesAndRequestsDockSyncWhenVisible()
        {
            var overlayHost = new FakeControlCentreOverlayHost();
            var controller = new MainWindowOverlayController(CreateLoadedStateController(), overlayHost);

            var requiresDockSync = controller.HandleNavigationCompleted(WindowState.Maximized, isActive: true);

            Assert.True(requiresDockSync);
            Assert.Equal(1, overlayHost.ShowCalls);
            Assert.Equal(1, overlayHost.PulseCalls);
        }

        [Fact]
        public void ApplyLayoutModeUsesCurrentShellState()
        {
            var stateController = CreateLoadedStateController();
            stateController.ToggleLayoutMode();
            var overlayHost = new FakeControlCentreOverlayHost();
            var controller = new MainWindowOverlayController(stateController, overlayHost);

            controller.ApplyLayoutMode();

            Assert.True(overlayHost.LayoutModeEnabled);
        }

        private static MainWindowShellStateController CreateLoadedStateController()
        {
            var controller = CreateStateController();
            controller.Load();
            return controller;
        }

        private static MainWindowShellStateController CreateStateController()
        {
            return new MainWindowShellStateController(new FakeControlCentreUiStateStore());
        }

        private sealed class FakeControlCentreOverlayHost : IControlCentreOverlayHost
        {
            public event EventHandler? Clicked { add { } remove { } }
            public event EventHandler? GeometryChanged { add { } remove { } }
            public event EventHandler? LayoutToggleRequested { add { } remove { } }

            public GeometryState LastAppliedGeometry { get; private set; }
            public GeometryState ClampedGeometry { get; set; }
            public int ShowCalls { get; private set; }
            public int HideCalls { get; private set; }
            public int PulseCalls { get; private set; }
            public int ClearTopmostCalls { get; private set; }
            public int ClampCalls { get; private set; }
            public bool LayoutModeEnabled { get; private set; }

            public void EnsureAttached(Window owner)
            {
            }

            public void Close()
            {
            }

            public void ShowIfNeeded(WindowState ownerState)
            {
                ShowCalls++;
            }

            public void Hide()
            {
                HideCalls++;
            }

            public void PulseTopmost()
            {
                PulseCalls++;
            }

            public void ClearTopmost()
            {
                ClearTopmostCalls++;
            }

            public void ApplyLayoutMode(bool enabled)
            {
                LayoutModeEnabled = enabled;
            }

            public void ApplyGeometry(GeometryState geometry)
            {
                LastAppliedGeometry = geometry;
            }

            public bool TryClampToVirtualScreen(out GeometryState geometry)
            {
                ClampCalls++;
                geometry = ClampedGeometry;
                return true;
            }
        }

        private sealed class FakeControlCentreUiStateStore : IControlCentreUiStateStore
        {
            public ControlCentreUiState State { get; private set; } = ControlCentreUiState.Default;

            public ControlCentreUiState Load() => State;

            public void Save(ControlCentreUiState state)
            {
                State = state;
            }
        }
    }
}
