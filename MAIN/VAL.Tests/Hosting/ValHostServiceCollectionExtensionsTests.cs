using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VAL.Host.Commands;
using VAL.Host.Hosting;
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
    }
}
