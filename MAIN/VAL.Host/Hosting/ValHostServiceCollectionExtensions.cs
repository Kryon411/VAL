using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VAL.Host.Commands;
using VAL.Host.Options;
using VAL.Host.Security;
using VAL.Host.Services;
using VAL.Host.WebMessaging;

namespace VAL.Host.Hosting
{
    public static class ValHostServiceCollectionExtensions
    {
        public static IServiceCollection AddValHost(
            this IServiceCollection services,
            IConfiguration configuration,
            Action<ValHostBuilder>? configure = null)
        {
            var builder = new ValHostBuilder(services, configuration);
            configure?.Invoke(builder);

            services.AddOptions<ValOptions>()
                .Bind(configuration.GetSection(ValOptions.SectionName))
                .PostConfigure(options => options.ApplyDefaults())
                .Validate(options => !string.IsNullOrWhiteSpace(options.DataRoot), "Val options must include a data root.");

            services.AddOptions<WebViewOptions>()
                .Bind(configuration.GetSection(WebViewOptions.SectionName))
                .PostConfigure(options => options.ApplyDefaults())
                .Validate(options => !string.IsNullOrWhiteSpace(options.StartUrl), "WebView options must include a start URL.");

            services.AddOptions<ModuleOptions>()
                .Bind(configuration.GetSection(ModuleOptions.SectionName))
                .PostConfigure(options => options.ApplyDefaults())
                .Validate(options => options.EnabledModules != null, "Module options must include module list.");

            services.AddSingleton(builder.CommandRegistry);
            services.AddSingleton(sp =>
                new HostCommandRouter(
                    sp.GetRequiredService<CommandRegistry>(),
                    sp.GetService<ICommandDiagnosticsReporter>()));

            services.AddSingleton<IWebViewSessionNonce, WebViewSessionNonce>();
            services.AddSingleton<IWebMessageSender, WebMessageSender>();
            services.AddSingleton<IAppPaths, AppPaths>();
            services.AddSingleton<IProcessLauncher, ProcessLauncher>();
            services.AddSingleton<IUiThread, UiThread>();
            services.AddSingleton<IBuildInfo, BuildInfo>();
            services.AddSingleton<IWebViewRuntime, WebViewRuntime>();

            return services;
        }
    }
}
