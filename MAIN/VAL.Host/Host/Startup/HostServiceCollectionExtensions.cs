using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VAL.Host.Options;
using VAL.Host.Security;
using VAL.Host.Services;
using VAL.Host.Services.Adapters;
using VAL.Host.WebMessaging;

namespace VAL.Host.Startup;

public static class HostServiceCollectionExtensions
{
    public static IServiceCollection AddValHost(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<ValOptions>()
            .Bind(configuration.GetSection(ValOptions.SectionName))
            .PostConfigure(options => options.ApplyDefaults())
            .Validate(options => !string.IsNullOrWhiteSpace(options.DataRoot), "Val options must include a data root.")
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<WebViewOptions>()
            .Bind(configuration.GetSection(WebViewOptions.SectionName))
            .PostConfigure(options => options.ApplyDefaults())
            .Validate(options => !string.IsNullOrWhiteSpace(options.StartUrl), "WebView options must include a start URL.")
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<ModuleOptions>()
            .Bind(configuration.GetSection(ModuleOptions.SectionName))
            .PostConfigure(options => options.ApplyDefaults())
            .Validate(options => options.EnabledModules != null, "Module options must include module list.")
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IModuleLoader, ModuleLoaderAdapter>();
        services.AddSingleton<ISessionContext, SessionContextAdapter>();
        services.AddSingleton<IOperationCoordinator, OperationCoordinatorAdapter>();
        services.AddSingleton<IAppPaths, AppPaths>();
        services.AddSingleton<IProcessLauncher, ProcessLauncher>();
        services.AddSingleton<IUiThread, UiThread>();
        services.AddSingleton<IBuildInfo, BuildInfo>();
        services.AddSingleton<IWebViewSessionNonce, WebViewSessionNonce>();
        services.AddSingleton<IWebViewRuntime, WebViewRuntime>();
        services.AddSingleton<IWebMessageSender, WebMessageSender>();
        return services;
    }
}
