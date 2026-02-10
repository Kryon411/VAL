using System;
using System.Reflection;
using System.Text.Json;
using VAL.Host.Commands;
using Xunit;

namespace VAL.Tests.Commands
{
    public sealed class CommandRegistryTests
    {
        private static readonly Uri TestSourceUri = new("https://chatgpt.com");
        private static readonly string[] RequiredEnabled = { "enabled" };

        [Fact]
        public void RegisterThrowsWhenCommandTypeIsDuplicated()
        {
            var registry = new CommandRegistry();
            registry.Register(new CommandSpec("cmd.sample", "Test", Array.Empty<string>(), _ => { }));

            var ex = Assert.Throws<InvalidOperationException>(() =>
                registry.Register(new CommandSpec("cmd.sample", "Test", Array.Empty<string>(), _ => { })));

            Assert.Contains("registered more than once", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void DispatchReturnsDeprecatedStatusWhenCommandIsFenced()
        {
            var registry = new CommandRegistry();
            registry.Register(new CommandSpec(
                "cmd.deprecated",
                "Test",
                Array.Empty<string>(),
                _ => { },
                IsDeprecated: true,
                DeprecationReason: "Deprecated for safety."));

            using var doc = JsonDocument.Parse("{}");
            var cmd = CreateHostCommand("cmd.deprecated", doc.RootElement);

            var result = registry.Dispatch(cmd);

            Assert.Equal(CommandDispatchStatus.RejectedDeprecated, result.Status);
            Assert.Equal("Deprecated for safety.", result.Detail);
            Assert.False(result.IsAccepted);
        }

        [Fact]
        public void DispatchReturnsMissingRequiredFieldStatus()
        {
            var registry = new CommandRegistry();
            registry.Register(new CommandSpec("cmd.required", "Test", RequiredEnabled, _ => { }));

            using var doc = JsonDocument.Parse("{}");
            var cmd = CreateHostCommand("cmd.required", doc.RootElement);

            var result = registry.Dispatch(cmd);

            Assert.Equal(CommandDispatchStatus.RejectedMissingRequiredField, result.Status);
            Assert.Contains("enabled", result.Detail, StringComparison.OrdinalIgnoreCase);
            Assert.False(result.IsAccepted);
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
