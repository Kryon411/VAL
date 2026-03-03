using System;
using System.Reflection;
using System.Text.Json;
using VAL.Contracts;
using VAL.Host;
using VAL.Host.Commands;
using Xunit;

namespace VAL.Tests.Commands
{
    public sealed class CommandRegistryFactoryTests
    {
        private static readonly Uri TestSourceUri = new("https://chatgpt.com");

        [Fact]
        public void RegisterCommandsBuildsRegistryWithoutThrowing()
        {
            var registry = new CommandRegistry();

            var ex = Record.Exception(() => RegisterAllCommands(registry));

            Assert.Null(ex);
        }

        [Fact]
        public void RegisterCommandsAcceptsNavigationCommand()
        {
            var registry = new CommandRegistry();
            RegisterAllCommands(registry);

            using var doc = JsonDocument.Parse("{}");
            var command = CreateHostCommand(WebCommandNames.NavCommandGoChat, doc.RootElement);

            var result = registry.Dispatch(command);

            Assert.Equal(CommandDispatchStatus.Accepted, result.Status);
            Assert.True(result.IsAccepted);
        }

        private static void RegisterAllCommands(CommandRegistry registry)
        {
            Action<HostCommand> noop = _ => { };

            CommandRegistryFactory.RegisterCommands(
                registry,
                handleContinuumCommand: noop,
                handleVoidSetEnabled: noop,
                handlePortalSetEnabled: noop,
                handlePortalOpenSnip: noop,
                handlePortalSendStaged: noop,
                handlePrivacySetContinuumLogging: noop,
                handlePrivacySetPortalCapture: noop,
                handlePrivacyOpenDataFolder: noop,
                handlePrivacyWipeData: noop,
                handleToolsOpenTruthHealth: noop,
                handleToolsOpenDiagnostics: noop,
                handleNavigationGoChat: noop,
                handleNavigationGoBack: noop,
                handleDockRequestModel: noop,
                handleDockUiStateGet: noop,
                handleDockUiStateSet: noop,
                handleAbyssOpenQueryUi: noop,
                handleAbyssSearch: noop,
                handleAbyssRetryLast: noop,
                handleAbyssInjectResult: noop,
                handleAbyssInjectResults: noop,
                handleAbyssLast: noop,
                handleAbyssOpenSource: noop,
                handleAbyssClearResults: noop,
                handleAbyssDisregard: noop,
                handleAbyssGetResults: noop,
                handleAbyssInjectPrompt: noop,
                handleAbyssInject: noop);
        }

        private static HostCommand CreateHostCommand(string type, JsonElement root)
        {
            var created = Activator.CreateInstance(
                typeof(HostCommand),
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                args: new object?[] { type, "{}", null, TestSourceUri, root },
                culture: null);

            return Assert.IsType<HostCommand>(created);
        }
    }
}
