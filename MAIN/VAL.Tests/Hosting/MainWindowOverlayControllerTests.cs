using System;
using System.Windows;

using VAL.App.Host.Services;
using VAL.App.State;
using VAL.Tests.Shared;

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
            var controller = new MainWindowOverlayController(stateController, overlayHost, CreateReadyRuntime());
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
            var controller = new MainWindowOverlayController(CreateLoadedStateController(), overlayHost, CreateReadyRuntime());

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
            var controller = new MainWindowOverlayController(stateController, overlayHost, CreateReadyRuntime());

            controller.HandleOwnerBoundsChanged();

            Assert.Equal(0, overlayHost.ClampCalls);
        }

        [Fact]
        public void HandleNavigationCompletedShowsPulsesAndRequestsDockSyncWhenVisible()
        {
            var overlayHost = new FakeControlCentreOverlayHost();
            var controller = new MainWindowOverlayController(CreateLoadedStateController(), overlayHost, CreateReadyRuntime());

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
            var controller = new MainWindowOverlayController(stateController, overlayHost, CreateReadyRuntime());

            controller.ApplyLayoutMode();

            Assert.True(overlayHost.LayoutModeEnabled);
        }

        private static MainWindowShellStateController CreateLoadedStateController()
        {
            var controller = CreateStateController();
            controller.Load();
            return controller;
        }

        [Fact]
        public void AutomaticAnchorFollowsHeaderButUserDragIsNotOverwritten()
        {
            var state = CreateLoadedStateController();
            var overlay = new FakeControlCentreOverlayHost();
            var controller = new MainWindowOverlayController(state, overlay, CreateReadyRuntime());
            var anchor = new GeometryState(100, 20, 40, 40);
            controller.InitializeGeometry(() => anchor);
            anchor = new GeometryState(200, 20, 40, 40);
            controller.HandleOwnerBoundsChanged();
            Assert.Equal(anchor, overlay.LastAppliedGeometry);
            Assert.False(state.IsControlCentreCustom);

            controller.HandleGeometryChanged();
            anchor = new GeometryState(300, 20, 40, 40);
            controller.HandleOwnerBoundsChanged();
            Assert.True(state.IsControlCentreCustom);
            Assert.NotEqual(anchor, overlay.LastAppliedGeometry);

            state.ResetPlacement();
            controller.HandleOwnerBoundsChanged();
            Assert.Equal(anchor, overlay.LastAppliedGeometry);
        }

        [Fact]
        public void HeaderUpdatesNeverInterruptPointerInteraction()
        {
            var overlay = new FakeControlCentreOverlayHost { IsInteracting = true };
            var controller = new MainWindowOverlayController(CreateLoadedStateController(), overlay, CreateReadyRuntime());
            controller.HandleOwnerBoundsChanged();
            Assert.Equal(0, overlay.ClampCalls);
        }

        [Fact]
        public void CustomLauncherMovesWithOwnerAndStaysInsideRestoredViewport()
        {
            var state = CreateLoadedStateController();
            var overlay = new FakeControlCentreOverlayHost { ReadAppliedGeometry = true };
            var controller = new MainWindowOverlayController(state, overlay, CreateReadyRuntime());
            var bounds = new Rect(1000, 100, 500, 400);
            controller.InitializeGeometry(() => new GeometryState(1100, 120, 40, 40), () => bounds);
            controller.HandleGeometryChanged();
            bounds = new Rect(1200, 200, 500, 400);
            controller.HandleOwnerBoundsChanged();
            Assert.Equal(new GeometryState(1300, 220, 40, 40), state.CreateSnapshot().ControlCentre);

            bounds = new Rect(1200, 200, 80, 80);
            controller.HandleOwnerBoundsChanged();
            Assert.Equal(new GeometryState(1240, 220, 40, 40), state.CreateSnapshot().ControlCentre);
            Assert.True(state.IsControlCentreCustom);
        }

        [Theory]
        [InlineData("activation")]
        [InlineData("restore")]
        [InlineData("navigation")]
        [InlineData("source")]
        [InlineData("bounds")]
        [InlineData("geometry")]
        public void DisarmedBridgeKeepsLauncherHiddenWithoutChangingSavedGeometry(string refresh)
        {
            var state = CreateLoadedStateController();
            var before = state.CreateSnapshot();
            var overlay = new FakeControlCentreOverlayHost();
            var controller = new MainWindowOverlayController(state, overlay, new TestWebViewRuntime { IsReady = true });

            switch (refresh)
            {
                case "activation": controller.HandleActivated(); break;
                case "restore": controller.HandleOwnerStateChanged(WindowState.Maximized); break;
                case "navigation": Assert.False(controller.HandleNavigationCompleted(WindowState.Maximized, true)); break;
                case "source": controller.HandleNavigationStateChanged(WindowState.Maximized); break;
                case "bounds": controller.HandleOwnerBoundsChanged(); break;
                case "geometry": controller.HandleGeometryChanged(); break;
            }

            Assert.Equal(1, overlay.HideCalls);
            Assert.Equal(0, overlay.ShowCalls);
            Assert.Equal(0, overlay.PulseCalls);
            Assert.Equal(0, overlay.ClampCalls);
            Assert.Equal(before.ControlCentre, state.CreateSnapshot().ControlCentre);
        }

        [Fact]
        public void ReturningToTrustedPageRestoresLauncherButMinimizedOwnerKeepsItHidden()
        {
            var runtime = new TestWebViewRuntime { IsReady = true };
            var overlay = new FakeControlCentreOverlayHost();
            var controller = new MainWindowOverlayController(CreateLoadedStateController(), overlay, runtime);

            controller.HandleNavigationStateChanged(WindowState.Maximized);
            runtime.IsBridgeReady = true;
            controller.HandleNavigationStateChanged(WindowState.Minimized);
            Assert.Equal(0, overlay.ShowCalls);
            controller.HandleNavigationStateChanged(WindowState.Maximized);

            Assert.Equal(2, overlay.HideCalls);
            Assert.Equal(1, overlay.ShowCalls);
        }

        private static TestWebViewRuntime CreateReadyRuntime() => new() { IsReady = true, IsBridgeReady = true };

        private static MainWindowShellStateController CreateStateController()
        {
            return new MainWindowShellStateController(new FakeControlCentreUiStateStore());
        }

        private sealed class FakeControlCentreOverlayHost : IControlCentreOverlayHost
        {
            public bool IsInteracting { get; set; }
            public bool ReadAppliedGeometry { get; set; }
            public event EventHandler? Clicked { add { } remove { } }
            public event EventHandler? GeometryChanged { add { } remove { } }
            public event EventHandler? LayoutToggleRequested { add { } remove { } }
            public event EventHandler? BrowserFocusRequested { add { } remove { } }
            public bool TryFocus() => false;

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

            public bool TryClampToVirtualScreen(out GeometryState geometry, Rect? viewportBounds = null)
            {
                ClampCalls++;
                geometry = ReadAppliedGeometry ? LastAppliedGeometry : ClampedGeometry;
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
