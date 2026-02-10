using System;
using System.Reflection;
using System.Text.Json;
using VAL.Contracts;
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

            var ex = Record.Exception(() =>
                CommandRegistryFactory.RegisterCommands(
                    registry,
                    _ => { },
                    _ => { },
                    _ => { },
                    _ => { },
                    _ => { },
                    _ => { },
                    _ => { },
                    _ => { },
                    _ => { },
                    _ => { },
                    _ => { },
                    _ => { },
                    _ => { },
                    _ => { },
                    _ => { },
                    _ => { },
                    _ => { },
                    _ => { },
                    _ => { },
                    _ => { },
                    _ => { },
                    _ => { },
                    _ => { },
                    _ => { },
                    _ => { },
                    _ => { },
                    _ => { }));

            Assert.Null(ex);
        }

        [Fact]
        public void RegisterCommandsFencesDeprecatedNavigationCommand()
        {
            var registry = new CommandRegistry();
            CommandRegistryFactory.RegisterCommands(
                registry,
                _ => { },
                _ => { },
                _ => { },
                _ => { },
                _ => { },
                _ => { },
                _ => { },
                _ => { },
                _ => { },
                _ => { },
                _ => { },
                _ => { },
                _ => { },
                _ => { },
                _ => { },
                _ => { },
                _ => { },
                _ => { },
                _ => { },
                _ => { },
                _ => { },
                _ => { },
                _ => { },
                _ => { },
                _ => { },
                _ => { },
                _ => { });

            using var doc = JsonDocument.Parse("{}");
            var command = CreateHostCommand(WebCommandNames.NavCommandGoChat, doc.RootElement);

            var result = registry.Dispatch(command);

            Assert.Equal(CommandDispatchStatus.RejectedDeprecated, result.Status);
            Assert.Contains("deprecated", result.Detail, StringComparison.OrdinalIgnoreCase);
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
