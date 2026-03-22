using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VAL.Host.Commands;

namespace VAL.Host.Hosting
{
    public sealed class ValHostBuilder
    {
        public ValHostBuilder(IServiceCollection services, IConfiguration configuration)
        {
            Services = services ?? throw new ArgumentNullException(nameof(services));
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            CommandRegistry = new CommandRegistry();
            CommandRegistryConfigurators = new List<Action<IServiceProvider, CommandRegistry>>();
        }

        public IServiceCollection Services { get; }
        public IConfiguration Configuration { get; }
        public CommandRegistry CommandRegistry { get; }
        public IList<Action<IServiceProvider, CommandRegistry>> CommandRegistryConfigurators { get; }
    }
}
