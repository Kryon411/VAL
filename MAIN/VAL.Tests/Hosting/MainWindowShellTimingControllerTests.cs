using System;
using System.Collections.Generic;

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
    public sealed class MainWindowShellTimingControllerTests
    {
        [Fact]
        public void LoadStateLoadsPersistedShellState()
        {
            var stateStore = new FakeControlCentreUiStateStore
            {
                StateToLoad = new ControlCentreUiState
                {
                    Dock = new DockGeometryState
                    {
                        IsOpen = true,
                        X = 120,
                        Y = 80,
                        W = 640,
                        H = 520,
                    },
                    LayoutMode = true,
                }
            };

            var (_, stateController, _, timingController, log) = CreateController(stateStore);

            timingController.LoadState();
            var snapshot = stateController.CreateSnapshot();

            Assert.True(snapshot.Dock.IsOpen);
            Assert.Equal(120, snapshot.Dock.X);
            Assert.Equal(80, snapshot.Dock.Y);
            Assert.Equal(640, snapshot.Dock.W);
            Assert.Equal(520, snapshot.Dock.H);
            Assert.True(snapshot.LayoutMode);
            Assert.Empty(log.Warnings);
        }

        [Fact]
        public void LoadStateLogsWarningWhenPersistenceThrows()
        {
            var stateStore = new FakeControlCentreUiStateStore
            {
                LoadException = new InvalidOperationException("boom")
            };

            var (_, _, _, timingController, log) = CreateController(stateStore);

            timingController.LoadState();

            Assert.Single(log.Warnings);
            Assert.Contains("Failed to load shell state.", log.Warnings[0], StringComparison.Ordinal);
        }

        [Fact]
        public void ScheduleStatePersistRestartsPersistAction()
        {
            var (_, _, factory, timingController, _) = CreateController(new FakeControlCentreUiStateStore());

            timingController.ScheduleStatePersist();

            Assert.Equal(1, factory.PersistAction.RestartCount);
            Assert.Equal(TimeSpan.FromMilliseconds(150), factory.PersistAction.Delay);
        }

        [Fact]
        public void PersistActionSavesCurrentShellStateWhenTriggered()
        {
            var stateStore = new FakeControlCentreUiStateStore();
            var (_, stateController, factory, timingController, _) = CreateController(stateStore);
            timingController.LoadState();
            stateController.ToggleLayoutMode();

            factory.PersistAction.Trigger();

            Assert.Equal(1, stateStore.SaveCallCount);
            Assert.NotNull(stateStore.LastSavedState);
            Assert.True(stateStore.LastSavedState!.LayoutMode);
        }

        [Fact]
        public void RequestDockStateSyncRestartsDockSyncAction()
        {
            var (_, _, factory, timingController, _) = CreateController(new FakeControlCentreUiStateStore());

            timingController.RequestDockStateSync();

            Assert.Equal(1, factory.DockStateSyncAction.RestartCount);
            Assert.Equal(TimeSpan.FromMilliseconds(450), factory.DockStateSyncAction.Delay);
        }

        [Fact]
        public void DockStateSyncActionPublishesDockStateWhenTriggered()
        {
            var stateStore = new FakeControlCentreUiStateStore();
            var (_, _, factory, timingController, _) = CreateController(stateStore, isWebViewReady: true);
            timingController.LoadState();

            factory.DockStateSyncAction.Trigger();

            Assert.Single(factory.Sender.Envelopes);
            Assert.Equal("dock.ui_state.data", factory.Sender.Envelopes[0].Name);
        }

        [Fact]
        public void FlushAndStopCancelsPendingActionsAndPersistsState()
        {
            var stateStore = new FakeControlCentreUiStateStore();
            var (_, stateController, factory, timingController, _) = CreateController(stateStore);
            timingController.LoadState();
            stateController.ToggleLayoutMode();

            timingController.FlushAndStop();

            Assert.Equal(1, factory.PersistAction.CancelCount);
            Assert.Equal(1, factory.DockStateSyncAction.CancelCount);
            Assert.Equal(1, stateStore.SaveCallCount);
            Assert.NotNull(stateStore.LastSavedState);
            Assert.True(stateStore.LastSavedState!.LayoutMode);
        }

        private static (
            MainWindowShellBridgeController BridgeController,
            MainWindowShellStateController StateController,
            FakeDeferredActionFactory ActionFactory,
            MainWindowShellTimingController TimingController,
            FakeLog Log) CreateController(
            FakeControlCentreUiStateStore stateStore,
            bool isWebViewReady = false)
        {
            var stateController = new MainWindowShellStateController(stateStore);
            var actionFactory = new FakeDeferredActionFactory();
            var log = new FakeLog();
            var bridgeController = new MainWindowShellBridgeController(
                stateController,
                new FakeWebViewRuntime { IsReady = isWebViewReady },
                actionFactory.Sender,
                log);
            var timingController = new MainWindowShellTimingController(
                actionFactory,
                stateController,
                bridgeController,
                log);

            return (bridgeController, stateController, actionFactory, timingController, log);
        }

        private sealed class FakeDeferredActionFactory : IDeferredActionFactory
        {
            private readonly List<FakeDeferredAction> _actions = [];

            public FakeWebMessageSender Sender { get; } = new();

            public FakeDeferredAction PersistAction => _actions[0];

            public FakeDeferredAction DockStateSyncAction => _actions[1];

            public IDeferredAction Create(TimeSpan interval, Action callback)
            {
                var action = new FakeDeferredAction(interval, callback);
                _actions.Add(action);
                return action;
            }
        }

        private sealed class FakeDeferredAction : IDeferredAction
        {
            private readonly Action _callback;

            public FakeDeferredAction(TimeSpan delay, Action callback)
            {
                Delay = delay;
                _callback = callback ?? throw new ArgumentNullException(nameof(callback));
            }

            public TimeSpan Delay { get; }

            public int RestartCount { get; private set; }

            public int CancelCount { get; private set; }

            public void Restart()
            {
                RestartCount++;
            }

            public void Cancel()
            {
                CancelCount++;
            }

            public void Trigger()
            {
                _callback();
            }
        }

        private sealed class FakeControlCentreUiStateStore : IControlCentreUiStateStore
        {
            public ControlCentreUiState StateToLoad { get; set; } = ControlCentreUiState.Default;

            public ControlCentreUiState? LastSavedState { get; private set; }

            public Exception? LoadException { get; set; }

            public int SaveCallCount { get; private set; }

            public ControlCentreUiState Load()
            {
                if (LoadException != null)
                {
                    throw LoadException;
                }

                return StateToLoad;
            }

            public void Save(ControlCentreUiState state)
            {
                SaveCallCount++;
                LastSavedState = state.Normalize();
            }
        }

        private sealed class FakeWebViewRuntime : IWebViewRuntime
        {
            public bool IsReady { get; set; }

            public CoreWebView2? Core => null;

            public Uri? LastChatUri => null;

            public event Action<WebMessageEnvelope>? WebMessageJsonReceived { add { } remove { } }

            public event Action? NavigationCompleted { add { } remove { } }

            public System.Threading.Tasks.Task InitializeAsync(WebView2 control) => System.Threading.Tasks.Task.CompletedTask;

            public void PostJson(string json)
            {
            }

            public System.Threading.Tasks.Task ExecuteScriptAsync(string js) => System.Threading.Tasks.Task.CompletedTask;

            public void Navigate(string url)
            {
            }

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
            public List<string> Warnings { get; } = [];

            public void Info(string category, string message)
            {
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
    }
}
