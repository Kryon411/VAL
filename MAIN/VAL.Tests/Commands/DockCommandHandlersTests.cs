using System;
using System.Reflection;
using System.Text.Json;

using VAL.App.Host.Commands;
using VAL.App.State;
using VAL.Contracts;
using VAL.Host;
using VAL.Host.Commands;
using VAL.Host.Services;
using VAL.Host.WebMessaging;

using Xunit;

namespace VAL.Tests.Commands
{
    public sealed class DockCommandHandlersTests
    {
        private static readonly Uri TestSourceUri = new("https://chatgpt.com");

        [Fact]
        public void HandleUiStateGetSendsControlCentreDockState()
        {
            var stateStore = new FakeControlCentreUiStateStore
            {
                State = new ControlCentreUiState
                {
                    Dock = new DockGeometryState
                    {
                        IsOpen = true,
                        X = 111,
                        Y = 222,
                        W = 640,
                        H = 480
                    }
                }
            };
            var sender = new FakeWebMessageSender();
            var handler = new DockCommandHandlers(new FakeDockModelService(), stateStore, sender, new FakeLog());

            using var doc = JsonDocument.Parse("{}");
            var command = CreateHostCommand(WebCommandNames.DockUiStateGet, doc.RootElement, chatId: "chat-1");

            handler.HandleUiStateGet(command);

            var envelope = Assert.IsType<MessageEnvelope>(sender.LastEnvelope);
            Assert.Equal(WebMessageTypes.Event, envelope.Type);
            Assert.Equal(WebCommandNames.DockUiStateGet, envelope.Name);
            Assert.Equal("chat-1", envelope.ChatId);
            Assert.True(envelope.Payload.HasValue);

            var payload = envelope.Payload!.Value;
            Assert.True(payload.GetProperty("isOpen").GetBoolean());
            Assert.Equal(111, payload.GetProperty("x").GetInt32());
            Assert.Equal(222, payload.GetProperty("y").GetInt32());
            Assert.Equal(640, payload.GetProperty("w").GetInt32());
            Assert.Equal(480, payload.GetProperty("h").GetInt32());
            Assert.Equal("shelf", payload.GetProperty("mode").GetString());
        }

        [Fact]
        public void HandleUiStateSetUpdatesControlCentreDockState()
        {
            var stateStore = new FakeControlCentreUiStateStore
            {
                State = new ControlCentreUiState()
            };
            var handler = new DockCommandHandlers(new FakeDockModelService(), stateStore, new FakeWebMessageSender(), new FakeLog());

            using var doc = JsonDocument.Parse("{\"isOpen\":true,\"x\":140,\"y\":90,\"w\":700,\"h\":530}");
            var command = CreateHostCommand(WebCommandNames.DockUiStateSet, doc.RootElement);

            handler.HandleUiStateSet(command);

            Assert.True(stateStore.State.Dock.IsOpen);
            Assert.Equal(140, stateStore.State.Dock.X);
            Assert.Equal(90, stateStore.State.Dock.Y);
            Assert.Equal(700, stateStore.State.Dock.W);
            Assert.Equal(530, stateStore.State.Dock.H);
        }

        private static HostCommand CreateHostCommand(string type, JsonElement root, string? chatId = null)
        {
            var created = Activator.CreateInstance(
                typeof(HostCommand),
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                args: new object?[] { type, root.GetRawText(), chatId, TestSourceUri, root },
                culture: null);

            return Assert.IsType<HostCommand>(created);
        }

        private sealed class FakeControlCentreUiStateStore : IControlCentreUiStateStore
        {
            public ControlCentreUiState State { get; set; } = ControlCentreUiState.Default;

            public ControlCentreUiState Load() => State;

            public void Save(ControlCentreUiState state)
            {
                State = state;
            }
        }

        private sealed class FakeDockModelService : IDockModelService
        {
            public void Publish(string? chatId = null) { }

            public void UpdatePortalState(bool enabled, bool privacyAllowed, int count) { }
        }

        private sealed class FakeWebMessageSender : IWebMessageSender
        {
            public MessageEnvelope? LastEnvelope { get; private set; }

            public void Send(MessageEnvelope envelope)
            {
                LastEnvelope = envelope;
            }
        }

        private sealed class FakeLog : ILog
        {
            public void Info(string category, string message) { }

            public void Warn(string category, string message) { }

            public void LogError(string category, string message) { }

            public void Verbose(string category, string message) { }
        }
    }
}
