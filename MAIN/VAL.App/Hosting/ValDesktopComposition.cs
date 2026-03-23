using System;
using System.Windows.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VAL.Continuum;
using VAL.Continuum.Pipeline;
using VAL.Continuum.Pipeline.QuickRefresh;
using VAL.Continuum.Pipeline.Telemetry;
using VAL.Continuum.Pipeline.Truth;
using VAL.Host;
using VAL.Host.Abyss;
using VAL.Host.Commands;
using VAL.Host.Hosting;
using VAL.Host.Portal;
using VAL.Host.Services;
using VAL.Host.Services.Adapters;
using VAL.Host.Startup;

namespace VAL.Hosting
{
    public static class ValDesktopComposition
    {
        public static IServiceCollection AddValDesktopApp(
            this IServiceCollection services,
            IConfiguration configuration,
            StartupOptions startupOptions,
            SmokeTestSettings smokeSettings,
            StartupCrashGuard crashGuard)
        {
            return ValHostServiceCollectionExtensions.AddValHost(
                services,
                configuration,
                startupOptions,
                smokeSettings,
                builder => ConfigureDesktopApp(builder, crashGuard));
        }

        public static void ConfigureDesktopApp(
            ValHostBuilder builder,
            StartupCrashGuard crashGuard)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(crashGuard);

            var desktopUiContext = new DesktopUiContext(Dispatcher.CurrentDispatcher);

            builder.Services.AddSingleton<IDesktopUiContext>(desktopUiContext);
            builder.Services.AddSingleton<ICommandDiagnosticsReporter, CommandDiagnosticsReporter>();
            builder.Services.AddSingleton<ContinuumHost>();
            builder.Services.AddSingleton<ContinuumCommandHandlers>();
            builder.Services.AddSingleton<ICommandRegistryContributor>(sp => sp.GetRequiredService<ContinuumCommandHandlers>());
            builder.Services.AddSingleton<VoidCommandHandlers>();
            builder.Services.AddSingleton<ICommandRegistryContributor>(sp => sp.GetRequiredService<VoidCommandHandlers>());
            builder.Services.AddSingleton<PortalCommandHandlers>();
            builder.Services.AddSingleton<ICommandRegistryContributor>(sp => sp.GetRequiredService<PortalCommandHandlers>());
            builder.Services.AddSingleton<PrivacyCommandHandlers>();
            builder.Services.AddSingleton<ICommandRegistryContributor>(sp => sp.GetRequiredService<PrivacyCommandHandlers>());
            builder.Services.AddSingleton<ToolsCommandHandlers>();
            builder.Services.AddSingleton<ICommandRegistryContributor>(sp => sp.GetRequiredService<ToolsCommandHandlers>());
            builder.Services.AddSingleton<NavigationCommandHandlers>();
            builder.Services.AddSingleton<ICommandRegistryContributor>(sp => sp.GetRequiredService<NavigationCommandHandlers>());
            builder.Services.AddSingleton<DockCommandHandlers>();
            builder.Services.AddSingleton<ICommandRegistryContributor>(sp => sp.GetRequiredService<DockCommandHandlers>());
            builder.Services.AddSingleton<AbyssSearchService>();
            builder.Services.AddSingleton<AbyssRuntime>();
            builder.Services.AddSingleton<AbyssCommandHandlers>();
            builder.Services.AddSingleton<ICommandRegistryContributor>(sp => sp.GetRequiredService<AbyssCommandHandlers>());
            builder.Services.AddSingleton<CommandRegistryComposer>();
            builder.CommandRegistryConfigurators.Add((serviceProvider, registry) =>
                serviceProvider.GetRequiredService<CommandRegistryComposer>().Register(registry));

            builder.Services.AddSingleton<ModuleLoaderService>();
            builder.Services.AddSingleton<IModuleLoader>(sp => sp.GetRequiredService<ModuleLoaderService>());
            builder.Services.AddSingleton<SessionContext>();
            builder.Services.AddSingleton<ISessionContext>(sp => sp.GetRequiredService<SessionContext>());
            builder.Services.AddSingleton<OperationCoordinator>();
            builder.Services.AddSingleton<IOperationCoordinator>(sp => sp.GetRequiredService<OperationCoordinator>());
            builder.Services.AddSingleton<DesktopToastService>();
            builder.Services.AddSingleton<IToastService>(sp => sp.GetRequiredService<DesktopToastService>());
            builder.Services.AddSingleton<ITruthTelemetryPublisher, TruthTelemetryPublisher>();
            builder.Services.AddSingleton<ITruthStore>(sp =>
                new TruthStore(
                    sp.GetRequiredService<ITruthTelemetryPublisher>(),
                    sp.GetRequiredService<IAppPaths>().MemoryChatsRoot));
            builder.Services.AddSingleton<ITruthViewBuilder, TruthViewBuilder>();
            builder.Services.AddSingleton<ToastLedgerService>();
            builder.Services.AddSingleton<IToastLedger>(sp => sp.GetRequiredService<ToastLedgerService>());
            builder.Services.AddSingleton<ToastHubService>();
            builder.Services.AddSingleton<IToastHub>(sp => sp.GetRequiredService<ToastHubService>());
            builder.Services.AddSingleton<ICommandDispatcher, CommandDispatcherAdapter>();
            builder.Services.AddSingleton<ICrashHandler, CrashHandler>();
            builder.Services.AddSingleton<IPrivacySettingsService, PrivacySettingsService>();
            builder.Services.AddSingleton<IDataWipeService, DataWipeService>();
            builder.Services.AddSingleton<IDiagnosticsWindowService, DiagnosticsWindowService>();
            builder.Services.AddSingleton<ITruthHealthReportService, TruthHealthReportService>();
            builder.Services.AddSingleton<ITruthHealthWindowService, TruthHealthWindowService>();
            builder.Services.AddSingleton<IDockModelService, DockModelService>();
            builder.Services.AddSingleton<IDockUiStateStore, DockUiStateStore>();
            builder.Services.AddSingleton<PortalRuntime>();
            builder.Services.AddSingleton<IPortalRuntimeStateManager, PortalRuntimeStateManager>();
            builder.Services.AddSingleton<IPortalRuntimeService, PortalRuntimeService>();
            builder.Services.AddSingleton<IModuleRuntimeService, ModuleRuntimeService>();
            builder.Services.AddSingleton<IQuickRefreshService, QuickRefreshService>();
            builder.Services.AddSingleton<VAL.Continuum.Pipeline.Inject.IContinuumInjectInbox, VAL.Continuum.Pipeline.Inject.ContinuumInjectInbox>();
            builder.Services.AddSingleton<TelemetryThresholdMonitor>();
            builder.Services.AddSingleton<IContinuumPump, ContinuumPump>();
            builder.Services.AddSingleton<SmokeTestRunner>();
            builder.Services.AddSingleton(crashGuard);
            builder.Services.AddSingleton<IStartupCrashGuard>(crashGuard);

            builder.Services.AddValAppShell();
        }
    }
}
