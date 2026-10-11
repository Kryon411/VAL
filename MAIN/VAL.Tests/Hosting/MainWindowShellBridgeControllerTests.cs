using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;

using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

using VAL.App.Host.Services;
using VAL.App.State;
using VAL.Contracts;
using VAL.Host;
using VAL.Host.Services;
using VAL.Host.WebMessaging;

using Xunit;

namespace VAL.Tests.Hosting
{
    public sealed class MainWindowShellBridgeControllerTests
    {
        private static readonly Uri TestSourceUri = new("https://chatgpt.com");
        private static readonly string[] ExpectedLayoutPublishMessages = ["dock.layout.enable", "dock.ui_state.data"];

        [Fact]
        public void TryHandleLauncherClickSendsDockOpenWhenWebViewIsReady()
        {
            var stateController = CreateStateController();
            stateController.Load();
            var runtime = new FakeWebViewRuntime { IsReady = true };
            var sender = new FakeWebMessageSender();
            var log = new FakeLog();
            var controller = new MainWindowShellBridgeController(stateController, runtime, sender, log);
            var now = new DateTime(2026, 7, 13, 12, 0, 0, DateTimeKind.Utc);

            var accepted = controller.TryHandleLauncherClick(now, out var requiresDockStateSync);

            Assert.True(accepted);
            Assert.True(requiresDockStateSync);
            Assert.Single(sender.Envelopes);
            Assert.Equal("dock.open", sender.Envelopes[0].Name);
            Assert.Empty(log.Infos);
        }

        [Fact]
        public void AcceptedLauncherClicksTransferFocusBeforeOpeningAndClosingTheDock()
        {
            var stateController = CreateStateController();
            stateController.Load();
            var sender = new FakeWebMessageSender();
            var controller = new MainWindowShellBridgeController(
                stateController, new FakeWebViewRuntime { IsReady = true }, sender, new FakeLog());
            var now = DateTime.UtcNow;
            var focusCount = 0;

            void FocusWebView()
            {
                Assert.Equal(focusCount, sender.Envelopes.Count);
                focusCount++;
            }

            Assert.True(controller.TryHandleLauncherClick(now, out _, FocusWebView));
            Assert.True(controller.TryHandleLauncherClick(now.AddMilliseconds(300), out _, FocusWebView));

            Assert.Equal(2, focusCount);
            Assert.Equal("dock.open", sender.Envelopes[0].Name);
            Assert.Equal("dock.close", sender.Envelopes[1].Name);
        }

        [Fact]
        public void DebouncedLauncherClickDoesNotTransferFocusOrSendAnotherMessage()
        {
            var stateController = CreateStateController();
            stateController.Load();
            var sender = new FakeWebMessageSender();
            var controller = new MainWindowShellBridgeController(
                stateController, new FakeWebViewRuntime { IsReady = true }, sender, new FakeLog());
            var now = DateTime.UtcNow;
            var focusCount = 0;

            Assert.True(controller.TryHandleLauncherClick(now, out _, () => focusCount++));
            Assert.False(controller.TryHandleLauncherClick(now.AddMilliseconds(100), out _, () => focusCount++));

            Assert.Equal(1, focusCount);
            Assert.Single(sender.Envelopes);
            Assert.True(stateController.IsDockOpen);
        }

        [Fact]
        public void DisarmedLauncherNeverTransfersFocus()
        {
            var controller = new MainWindowShellBridgeController(
                CreateStateController(),
                new FakeWebViewRuntime { IsReady = true, BridgeEnabled = false },
                new FakeWebMessageSender(), new FakeLog());
            var focused = false;

            Assert.False(controller.TryHandleLauncherClick(DateTime.UtcNow, out _, () => focused = true));

            Assert.False(focused);
        }

        [Fact]
        public void DisarmedLauncherDoesNotTogglePersistedDockOrConsumeTheNextClick()
        {
            var stateController = CreateStateController();
            stateController.Load();
            var originalOpen = stateController.IsDockOpen;
            var runtime = new FakeWebViewRuntime { IsReady = true, BridgeEnabled = false };
            var sender = new FakeWebMessageSender();
            var controller = new MainWindowShellBridgeController(stateController, runtime, sender, new FakeLog());
            var now = DateTime.UtcNow;

            Assert.False(controller.TryHandleLauncherClick(now, out var sync));
            Assert.False(sync);
            Assert.Equal(originalOpen, stateController.IsDockOpen);
            Assert.Empty(sender.Envelopes);

            runtime.BridgeEnabled = true;
            Assert.True(controller.TryHandleLauncherClick(now, out _));
            Assert.NotEqual(originalOpen, stateController.IsDockOpen);
            Assert.Single(sender.Envelopes);
        }

        [Fact]
        public void DockStateIsNotPublishedOnAnAuthenticationPage()
        {
            var runtime = new FakeWebViewRuntime { IsReady = true, BridgeEnabled = false };
            var sender = new FakeWebMessageSender();
            var controller = new MainWindowShellBridgeController(CreateStateController(), runtime, sender, new FakeLog());

            controller.SendDockUiState();
            controller.PublishLayoutMode();

            Assert.Empty(sender.Envelopes);
        }

        [Fact]
        public void SendDockUiStateLogsNotReadyOnlyOnceUntilWebViewRecovers()
        {
            var stateController = CreateStateController();
            stateController.Load();
            var runtime = new FakeWebViewRuntime { IsReady = false };
            var sender = new FakeWebMessageSender();
            var log = new FakeLog();
            var controller = new MainWindowShellBridgeController(stateController, runtime, sender, log);

            controller.SendDockUiState();
            controller.SendDockUiState();

            Assert.Empty(sender.Envelopes);
            Assert.Single(log.Infos);
            Assert.Contains("Shell message ignored because the WebView2 bridge is not ready.", log.Infos[0]);

            runtime.IsReady = true;
            controller.SendDockUiState();

            Assert.Single(sender.Envelopes);
            Assert.Equal("dock.ui_state.data", sender.Envelopes[0].Name);

            runtime.IsReady = false;
            controller.SendDockUiState();

            Assert.Equal(2, log.Infos.Count(info => info.Contains("Shell message ignored because the WebView2 bridge is not ready.", StringComparison.Ordinal)));
        }

        [Fact]
        public void PublishLayoutModeSendsLayoutAndDockState()
        {
            var stateController = CreateStateController();
            stateController.Load();
            stateController.ToggleLayoutMode();
            var runtime = new FakeWebViewRuntime { IsReady = true };
            var sender = new FakeWebMessageSender();
            var log = new FakeLog();
            var controller = new MainWindowShellBridgeController(stateController, runtime, sender, log);

            controller.PublishLayoutMode();

            Assert.Equal(
                ExpectedLayoutPublishMessages,
                sender.Envelopes.Select(envelope => envelope.Name).ToArray());
        }

        [Fact]
        public void BuildNavigationRefreshPlanReturnsNoneForMinimizedWindows()
        {
            var plan = ShellNavigationRefreshPlan.For(WindowState.Minimized, isActive: true);

            Assert.Equal(ShellNavigationRefreshPlan.None, plan);
        }

        [Fact]
        public void BuildNavigationRefreshPlanRequestsOverlayRefreshForVisibleWindows()
        {
            var plan = ShellNavigationRefreshPlan.For(WindowState.Maximized, isActive: true);

            Assert.True(plan.ShowOverlay);
            Assert.True(plan.PulseTopmost);
            Assert.True(plan.RequestDockStateSync);
        }

        [Fact]
        public void TryHandleDockMessageUpdatesStoredDockState()
        {
            var stateController = CreateStateController();
            stateController.Load();
            var controller = CreateController(stateController, isReady: true);
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

            var changed = controller.TryHandleDockMessage(envelope, new Rect(0, 0, 1600, 900));
            var snapshot = stateController.CreateSnapshot();

            Assert.True(changed);
            Assert.True(snapshot.Dock.IsOpen);
            Assert.Equal(140, snapshot.Dock.X);
            Assert.Equal(90, snapshot.Dock.Y);
            Assert.Equal(700, snapshot.Dock.W);
            Assert.Equal(530, snapshot.Dock.H);
        }

        private static MainWindowShellBridgeController CreateController(bool isReady)
        {
            var stateController = CreateStateController();
            stateController.Load();
            return CreateController(stateController, isReady);
        }

        private static MainWindowShellBridgeController CreateController(
            MainWindowShellStateController stateController,
            bool isReady)
        {
            return new MainWindowShellBridgeController(
                stateController,
                new FakeWebViewRuntime { IsReady = isReady },
                new FakeWebMessageSender(),
                new FakeLog());
        }

        private static MainWindowShellStateController CreateStateController()
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

        private sealed class FakeWebViewRuntime : IWebViewRuntime
        {
            public bool IsReady { get; set; }
            public bool BridgeEnabled { get; set; } = true;
            public bool IsBridgeReady => IsReady && BridgeEnabled;
            public long NavigationGeneration => 0;
            public CoreWebView2? Core => null;
            public Uri? LastChatUri => null;
            public event Action<WebMessageEnvelope>? WebMessageJsonReceived { add { } remove { } }
            public event Action? NavigationCompleted { add { } remove { } }
            public event Action? NavigationStateChanged { add { } remove { } }

            public System.Threading.Tasks.Task InitializeAsync(WebView2 control) => System.Threading.Tasks.Task.CompletedTask;
            public void PostJson(string json) { }
            public System.Threading.Tasks.Task ExecuteScriptAsync(string js) => System.Threading.Tasks.Task.CompletedTask;
            public System.Threading.Tasks.Task<string?> EvaluateScriptAsync(string js) => System.Threading.Tasks.Task.FromResult<string?>(null);
            public void Navigate(string url) { }
            public bool TryGoBack() => false;
        }

        private sealed class FakeWebMessageSender : IWebMessageSender
        {
            public List<MessageEnvelope> Envelopes { get; } = [];

            public void Send(MessageEnvelope envelope)
            {
                Envelopes.Add(envelope);
            }
        }

        private sealed class FakeLog : ILog
        {
            public List<string> Infos { get; } = [];
            public List<string> Warnings { get; } = [];

            public void Info(string category, string message)
            {
                Infos.Add($"{category}: {message}");
            }

            public void Warn(string category, string message)
            {
                Warnings.Add($"{category}: {message}");
            }

            public void LogError(string category, string message)
            {
            }

            public void Verbose(string category, string message)
            {
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
