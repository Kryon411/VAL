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
        IConfiguration configuration,
        SmokeTestSettings smokeTestSettings,
        StartupOptions startupOptions,
        StartupCrashGuard crashGuard)
    {
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

        services.AddSingleton<IModuleLoader, ModuleLoaderAdapter>();
        services.AddSingleton<ISessionContext, SessionContextAdapter>();
        services.AddSingleton<IOperationCoordinator, OperationCoordinatorAdapter>();
        services.AddSingleton<IToastService, ToastServiceAdapter>();
        services.AddSingleton<IToastHub, ToastHubAdapter>();
        services.AddSingleton<ICommandDispatcher, CommandDispatcherAdapter>();
        services.AddSingleton<IAppPaths, AppPaths>();
        services.AddSingleton<IProcessLauncher, ProcessLauncher>();
        services.AddSingleton<IUiThread, UiThread>();
        services.AddSingleton<IBuildInfo, BuildInfo>();
        services.AddSingleton<IPrivacySettingsService, PrivacySettingsService>();
        services.AddSingleton<IDataWipeService, DataWipeService>();
        services.AddSingleton<ITruthHealthReportService, TruthHealthReportService>();
        services.AddSingleton<IDockModelService, DockModelService>();
        services.AddSingleton<IPortalRuntimeStateManager, PortalRuntimeStateManager>();
        services.AddSingleton<IPortalRuntimeService, PortalRuntimeService>();
        services.AddSingleton<IModuleRuntimeService, ModuleRuntimeService>();
        services.AddSingleton<VAL.Continuum.Pipeline.Truth.IContinuumWriter, VAL.Continuum.Pipeline.Truth.ContinuumWriter>();
        services.AddSingleton<VAL.Continuum.Pipeline.Inject.IContinuumInjectInbox, VAL.Continuum.Pipeline.Inject.ContinuumInjectInbox>();
        services.AddSingleton<IContinuumPump, ContinuumPump>();
        services.AddSingleton<IWebViewSessionNonce, WebViewSessionNonce>();
        services.AddSingleton<IWebViewRuntime, WebViewRuntime>();
        services.AddSingleton<IWebMessageSender, WebMessageSender>();
        services.AddSingleton(smokeTestSettings);
        services.AddSingleton<SmokeTestState>();
        services.AddSingleton<SmokeTestRunner>();
        services.AddSingleton(startupOptions);
        services.AddSingleton(crashGuard);

        return services;
    }
}
