using System;
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
        }

        public IServiceCollection Services { get; }
        public IConfiguration Configuration { get; }
        public CommandRegistry CommandRegistry { get; }
    }
}
