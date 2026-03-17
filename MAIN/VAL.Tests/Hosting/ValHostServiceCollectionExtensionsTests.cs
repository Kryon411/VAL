using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VAL.Host.Commands;
using VAL.Host.Hosting;
using VAL.Host.Services;
using VAL.Host.Startup;
using Xunit;

namespace VAL.Tests.Hosting
{
    public sealed class ValHostServiceCollectionExtensionsTests
    {
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
        }
    }
}
