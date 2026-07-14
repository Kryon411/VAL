using System;
using System.Text.Json;
using System.Windows;

using VAL.App.Host.Services;
using VAL.App.State;
using VAL.Contracts;
using VAL.Host.WebMessaging;

using Xunit;

namespace VAL.Tests.Hosting
{
    public sealed class MainWindowShellStateControllerTests
    {
        private static readonly Uri TestSourceUri = new("https://chatgpt.com");

        [Fact]
        public void TryHandleLauncherClickTogglesDockAndDebouncesRapidRepeat()
        {
            var controller = CreateController();
            controller.Load();
            var now = new DateTime(2026, 7, 13, 12, 0, 0, DateTimeKind.Utc);

            var firstAccepted = controller.TryHandleLauncherClick(now, out var firstEnvelope, out var firstRequiresSync);
            var secondAccepted = controller.TryHandleLauncherClick(now.AddMilliseconds(100), out _, out _);
            var thirdAccepted = controller.TryHandleLauncherClick(now.AddMilliseconds(400), out var thirdEnvelope, out var thirdRequiresSync);

            Assert.True(firstAccepted);
            Assert.Equal(WebMessageTypes.Event, firstEnvelope.Type);
            Assert.Equal("dock.open", firstEnvelope.Name);
            Assert.True(firstRequiresSync);

            Assert.False(secondAccepted);

            Assert.True(thirdAccepted);
            Assert.Equal("dock.close", thirdEnvelope.Name);
            Assert.False(thirdRequiresSync);
        }

        [Fact]
        public void ToggleLayoutModeBuildsExpectedEventAndUpdatesState()
        {
            var controller = CreateController();
            controller.Load();

            var enableEnvelope = controller.ToggleLayoutMode();
            var disableEnvelope = controller.ToggleLayoutMode();

            Assert.Equal("dock.layout.enable", enableEnvelope.Name);
            Assert.Equal("dock.layout.disable", disableEnvelope.Name);
            Assert.False(controller.IsLayoutModeEnabled);
        }

        [Fact]
        public void ResolveControlCentreGeometryCachesDefaultGeometry()
        {
            var controller = CreateController();
            controller.Load();
            var expected = new GeometryState(100, 120, 40, 40);

            var first = controller.ResolveControlCentreGeometry(() => expected);
            var second = controller.ResolveControlCentreGeometry(() => new GeometryState(1, 2, 3, 4));

            Assert.Equal(expected.X, first.X);
            Assert.Equal(expected.Y, first.Y);
            Assert.Equal(expected.W, first.W);
            Assert.Equal(expected.H, first.H);
            Assert.Equal(first.X, second.X);
            Assert.Equal(first.Y, second.Y);
        }

        [Fact]
        public void TryApplyDockMessageUpdatesDockStateAndClampsGeometry()
        {
            var controller = CreateController();
            controller.Load();
            var envelope = CreateWebMessageEnvelope(
                "dock.ui_state.set",
                new
                {
                    isOpen = true,
                    x = 2000,
                    y = 1200,
                    w = 900,
                    h = 700
                });

            var changed = controller.TryApplyDockMessage(envelope, new Rect(0, 0, 800, 600));
            var snapshot = controller.CreateSnapshot();

            Assert.True(changed);
            Assert.True(snapshot.Dock.IsOpen);
            Assert.Equal(800, snapshot.Dock.W);
            Assert.Equal(600, snapshot.Dock.H);
            Assert.Equal(0, snapshot.Dock.X);
            Assert.Equal(0, snapshot.Dock.Y);
        }

        [Fact]
        public void CreateDockUiStateEnvelopeReflectsStoredState()
        {
            var controller = CreateController();
            controller.Load();
            var envelope = CreateWebMessageEnvelope(
                "dock.ui_state.set",
                new
                {
                    isOpen = true,
                    x = 140,
                    y = 90,
                    w = 700,
                    h = 530
                });

            controller.TryApplyDockMessage(envelope, new Rect(0, 0, 1600, 900));
            var outbound = controller.CreateDockUiStateEnvelope();

            Assert.Equal("dock.ui_state.data", outbound.Name);
            Assert.True(outbound.Payload.HasValue);
            Assert.True(outbound.Payload.Value.GetProperty("isOpen").GetBoolean());
            Assert.Equal(140, outbound.Payload.Value.GetProperty("x").GetDouble());
            Assert.Equal(90, outbound.Payload.Value.GetProperty("y").GetDouble());
            Assert.Equal(700, outbound.Payload.Value.GetProperty("w").GetDouble());
            Assert.Equal(530, outbound.Payload.Value.GetProperty("h").GetDouble());
        }

        private static MainWindowShellStateController CreateController()
        {
            return new MainWindowShellStateController(new FakeControlCentreUiStateStore());
        }

        private static WebMessageEnvelope CreateWebMessageEnvelope(string name, object payload)
        {
            var parsedEnvelope = new MessageEnvelope
            {
                Type = WebMessageTypes.Command,
                Name = name,
                Payload = JsonSerializer.SerializeToElement(payload),
                Source = "dock",
                Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            };

            return new WebMessageEnvelope("{}", TestSourceUri, parsedEnvelope);
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
