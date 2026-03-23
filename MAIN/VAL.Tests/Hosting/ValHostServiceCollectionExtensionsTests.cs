using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VAL.Contracts;
using VAL.Host;
using VAL.Host.Commands;
using VAL.Host.Hosting;
using VAL.Host.Logging;
using VAL.Host.Services;
using VAL.Host.Startup;
using Xunit;

namespace VAL.Tests.Hosting
{
    public sealed class ValHostServiceCollectionExtensionsTests
    {
        private static readonly Uri TestSourceUri = new("https://chatgpt.com");

        [Fact]
        public void AddValHostBuildsServiceProviderWithoutThrowing()
        {
            var services = new ServiceCollection();
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["VAL:DataRoot"] = "./data",
                    ["WebView:StartUrl"] = "https://chatgpt.com",
                })
                .Build();

            services.AddValHost(configuration, builder =>
            {
                builder.CommandRegistry.Register(new CommandSpec("cmd.test", "Tests", Array.Empty<string>(), _ => { }));
            });

            var ex = Record.Exception(() => services.BuildServiceProvider(validateScopes: true));

            Assert.Null(ex);
        }

        [Fact]
        public void FullAddValHostRegistersStartupAndSmokeState()
        {
            var services = new ServiceCollection();
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["VAL:DataRoot"] = "./data",
                    ["WebView:StartUrl"] = "https://chatgpt.com",
                })
                .Build();
            var startupOptions = new StartupOptions(false, false, Array.Empty<string>());
            var smokeSettings = new SmokeTestSettings(false, TimeSpan.FromSeconds(15), null);

            services.AddValHost(configuration, startupOptions, smokeSettings, builder =>
            {
                builder.CommandRegistry.Register(new CommandSpec("cmd.test", "Tests", Array.Empty<string>(), _ => { }));
            });

            using var provider = services.BuildServiceProvider(validateScopes: true);

            Assert.Same(startupOptions, provider.GetRequiredService<StartupOptions>());
            Assert.Same(smokeSettings, provider.GetRequiredService<SmokeTestSettings>());
            Assert.NotNull(provider.GetRequiredService<SmokeTestState>());
            Assert.NotNull(provider.GetRequiredService<ILog>());
            Assert.NotNull(provider.GetRequiredService<ILogBootstrapper>());
        }

        [Fact]
        public void AddValHostAppliesDeferredCommandRegistryConfigurators()
        {
            var services = new ServiceCollection();
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["VAL:DataRoot"] = "./data",
                    ["WebView:StartUrl"] = "https://chatgpt.com",
                })
                .Build();

            services.AddValHost(configuration, builder =>
            {
                builder.CommandRegistryConfigurators.Add((_, registry) =>
                {
                    registry.Register(new CommandSpec("cmd.deferred", "Tests", Array.Empty<string>(), _ => { }));
                });
            });

            using var provider = services.BuildServiceProvider(validateScopes: true);
            var registry = provider.GetRequiredService<CommandRegistry>();

            using var doc = JsonDocument.Parse("{}");
            var command = CreateHostCommand("cmd.deferred", doc.RootElement);

            var result = registry.Dispatch(command);

            Assert.Equal(CommandDispatchStatus.Accepted, result.Status);
            Assert.True(result.IsAccepted);
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
