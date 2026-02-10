using System;
using System.Text.Json;
using VAL.Host.Commands;
using Xunit;

namespace VAL.Tests.Commands
{
    public sealed class CommandRegistryTests
    {
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
            var cmd = new HostCommand("cmd.deprecated", "{}", null, new Uri("https://chatgpt.com"), doc.RootElement);

            var result = registry.Dispatch(cmd);

            Assert.Equal(CommandDispatchStatus.RejectedDeprecated, result.Status);
            Assert.Equal("Deprecated for safety.", result.Detail);
            Assert.False(result.IsAccepted);
        }

        [Fact]
        public void DispatchReturnsMissingRequiredFieldStatus()
        {
            var registry = new CommandRegistry();
            registry.Register(new CommandSpec("cmd.required", "Test", new[] { "enabled" }, _ => { }));

            using var doc = JsonDocument.Parse("{}");
            var cmd = new HostCommand("cmd.required", "{}", null, new Uri("https://chatgpt.com"), doc.RootElement);

            var result = registry.Dispatch(cmd);

            Assert.Equal(CommandDispatchStatus.RejectedMissingRequiredField, result.Status);
            Assert.Contains("enabled", result.Detail, StringComparison.OrdinalIgnoreCase);
            Assert.False(result.IsAccepted);
        }
    }
}
