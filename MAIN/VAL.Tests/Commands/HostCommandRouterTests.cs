using System;

using VAL.Contracts;
using VAL.Host;
using VAL.Host.Commands;
using VAL.Host.WebMessaging;

using Xunit;

namespace VAL.Tests.Commands
{
    public sealed class HostCommandRouterTests
    {
        private static readonly Uri TestSourceUri = new("https://chatgpt.com");
        private static readonly string[] RequiredTestFields = { "required" };

        [Fact]
        public void BlockedAttachAndDelayedCompletionCannotChangeActiveChat()
        {
            var session = new SessionContext();
            session.SetActiveChatId("current");
            var registry = new CommandRegistry();
            registry.Register(new CommandSpec(WebCommandNames.ContinuumSessionAttach,
                "Continuum", RequiredTestFields, _ => { }));
            var router = new HostCommandRouter(registry, new FakeLog(), sessionContext: session);
            var json = "{\"type\":\"continuum.session.attach\",\"chatId\":\"stale\"}";
            Assert.Equal(HostCommandExecutionStatus.Blocked,
                router.HandleWebMessage(new WebMessageEnvelope(json, TestSourceUri)).Status);
            Assert.Equal("current", session.ActiveChatId);
            session.Observe(WebCommandNames.ContinuumChronicleDone, "stale");
            Assert.Equal("current", session.ActiveChatId);
            session.Observe(WebCommandNames.ContinuumSessionAttached, "new");
            Assert.Equal("new", session.ActiveChatId);
        }

        [Fact]
        public void HandleWebMessageReturnsBlockedResultForEmptyJson()
        {
            var router = CreateRouter(_ => { });

            var result = router.HandleWebMessage(new WebMessageEnvelope(string.Empty, TestSourceUri));

            Assert.Equal(HostCommandExecutionStatus.Blocked, result.Status);
            Assert.False(result.IsDockInvocation);
            Assert.Contains("empty", result.Reason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void HandleWebMessageReturnsBlockedResultForDeprecatedDockCommand()
        {
            var router = CreateRouter(registry =>
            {
                registry.Register(new CommandSpec(
                    WebCommandNames.NavCommandGoBack,
                    "Navigation",
                    Array.Empty<string>(),
                    _ => { },
                    IsDeprecated: true,
                    DeprecationReason: "Deprecated."));
            });

            const string json = """
                                {"type":"command","name":"nav.command.go_back","source":"dock","payload":{}}
                                """;
            var result = router.HandleWebMessage(new WebMessageEnvelope(json, TestSourceUri));

            Assert.Equal(HostCommandExecutionStatus.Blocked, result.Status);
            Assert.True(result.IsDockInvocation);
            Assert.Equal(WebCommandNames.NavCommandGoBack, result.CommandName);
        }

        [Fact]
        public void HandleWebMessageReturnsErrorResultForHandlerException()
        {
            var router = CreateRouter(registry =>
            {
                registry.Register(new CommandSpec(
                    WebCommandNames.ToolsOpenTruthHealth,
                    "Tools",
                    Array.Empty<string>(),
                    _ => throw new InvalidOperationException("boom")));
            });

            const string json = """
                                {"type":"command","name":"tools.open_truth_health","source":"dock","payload":{}}
                                """;
            var result = router.HandleWebMessage(new WebMessageEnvelope(json, TestSourceUri));

            Assert.Equal(HostCommandExecutionStatus.Error, result.Status);
            Assert.True(result.IsDockInvocation);
            Assert.NotNull(result.Exception);
        }

        [Fact]
        public void HandleWebMessageReturnsSuccessForAcceptedCommand()
        {
            var invoked = false;
            var router = CreateRouter(registry =>
            {
                registry.Register(new CommandSpec(
                    WebCommandNames.ToolsOpenTruthHealth,
                    "Tools",
                    Array.Empty<string>(),
                    _ => invoked = true));
            });

            const string json = """
                                {"type":"command","name":"tools.open_truth_health","source":"dock","payload":{}}
                                """;
            var result = router.HandleWebMessage(new WebMessageEnvelope(json, TestSourceUri));

            Assert.Equal(HostCommandExecutionStatus.Success, result.Status);
            Assert.True(invoked);
        }

        [Fact]
        public void HandleWebMessageUsesPreParsedEnvelopeWhenAvailable()
        {
            var invoked = false;
            var router = CreateRouter(registry =>
            {
                registry.Register(new CommandSpec(
                    WebCommandNames.ToolsOpenTruthHealth,
                    "Tools",
                    Array.Empty<string>(),
                    _ => invoked = true));
            });

            const string json = """
                                {"type":"command","name":"tools.open_truth_health","source":"dock","payload":{}}
                                """;
            Assert.True(MessageEnvelope.TryParse(json, out var parsedEnvelope));

            var result = router.HandleWebMessage(new WebMessageEnvelope(json, TestSourceUri, parsedEnvelope));

            Assert.Equal(HostCommandExecutionStatus.Success, result.Status);
            Assert.True(result.IsDockInvocation);
            Assert.True(invoked);
        }

        private static HostCommandRouter CreateRouter(Action<CommandRegistry> register)
        {
            var registry = new CommandRegistry();
            register(registry);
            return new HostCommandRouter(registry, new FakeLog());
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
