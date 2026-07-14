using System;
using System.Windows.Threading;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using VAL.App.Host;
using VAL.App.Host.Commands;
using VAL.App.Host.Services;
using VAL.App.Host.Services.Adapters;
using VAL.App.Host.Startup;
using VAL.App.State;
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
using VAL.Host.Startup;

namespace VAL.App.Hosting
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

            var services = builder.Services;
            var desktopUiContext = new DesktopUiContext(Dispatcher.CurrentDispatcher);

            RegisterDesktopContext(services, desktopUiContext, crashGuard);
            RegisterCommandServices(builder);
            RegisterDesktopRuntimeServices(services);

            services.AddValAppShell();
        }

        private static void RegisterDesktopContext(
            IServiceCollection services,
            DesktopUiContext desktopUiContext,
            StartupCrashGuard crashGuard)
        {
            services.AddSingleton<IDesktopUiContext>(desktopUiContext);
            services.AddSingleton(crashGuard);
            services.AddSingleton<IStartupCrashGuard>(crashGuard);
        }

        private static void RegisterCommandServices(ValHostBuilder builder)
        {
            var services = builder.Services;

            services.AddSingleton<ICommandDiagnosticsReporter, CommandDiagnosticsReporter>();
            services.AddSingleton<ContinuumHost>();
            AddCommandContributor<ContinuumCommandHandlers>(services);
            AddCommandContributor<VoidCommandHandlers>(services);
            AddCommandContributor<PortalCommandHandlers>(services);
            AddCommandContributor<PrivacyCommandHandlers>(services);
            AddCommandContributor<ToolsCommandHandlers>(services);
            AddCommandContributor<NavigationCommandHandlers>(services);
            AddCommandContributor<DockCommandHandlers>(services);
            services.AddSingleton<AbyssSearchService>();
            services.AddSingleton<AbyssRuntime>();
            AddCommandContributor<AbyssCommandHandlers>(services);
            services.AddSingleton<CommandRegistryComposer>();
            builder.CommandRegistryConfigurators.Add((serviceProvider, registry) =>
                serviceProvider.GetRequiredService<CommandRegistryComposer>().Register(registry));
        }

        private static void RegisterDesktopRuntimeServices(IServiceCollection services)
        {
            services.AddSingleton<ModuleLoaderService>();
            services.AddSingleton<IModuleLoader>(sp => sp.GetRequiredService<ModuleLoaderService>());
            services.AddSingleton<SessionContext>();
            services.AddSingleton<ISessionContext>(sp => sp.GetRequiredService<SessionContext>());
            services.AddSingleton<OperationCoordinator>();
            services.AddSingleton<IOperationCoordinator>(sp => sp.GetRequiredService<OperationCoordinator>());
            services.AddSingleton<DesktopToastService>();
            services.AddSingleton<IToastService>(sp => sp.GetRequiredService<DesktopToastService>());
            services.AddSingleton<ITruthTelemetryPublisher, TruthTelemetryPublisher>();
            services.AddSingleton<ITruthStore>(sp =>
                new TruthStore(
                    sp.GetRequiredService<ITruthTelemetryPublisher>(),
                    sp.GetRequiredService<IAppPaths>().MemoryChatsRoot));
            services.AddSingleton<ITruthViewBuilder, TruthViewBuilder>();
            services.AddSingleton<ToastLedgerService>();
            services.AddSingleton<IToastLedger>(sp => sp.GetRequiredService<ToastLedgerService>());
            services.AddSingleton<ToastHubService>();
            services.AddSingleton<IToastHub>(sp => sp.GetRequiredService<ToastHubService>());
            services.AddSingleton<ICommandDispatcher, CommandDispatcherAdapter>();
            services.AddSingleton<ICrashHandler, CrashHandler>();
            services.AddSingleton<ICrashWindowService, CrashWindowService>();
            services.AddSingleton<IPrivacySettingsService, PrivacySettingsService>();
            services.AddSingleton<IDataWipeService, DataWipeService>();
            services.AddSingleton<IDiagnosticsWindowService, DiagnosticsWindowService>();
            services.AddSingleton<ITruthHealthReportService, TruthHealthReportService>();
            services.AddSingleton<ITruthHealthWindowService, TruthHealthWindowService>();
            services.AddSingleton<IDockModelService, DockModelService>();
            services.AddSingleton<PortalRuntime>();
            services.AddSingleton<IPortalRuntimeStateManager, PortalRuntimeStateManager>();
            services.AddSingleton<IPortalRuntimeService, PortalRuntimeService>();
            services.AddSingleton<IModuleRuntimeService, ModuleRuntimeService>();
            services.AddSingleton<IQuickRefreshService, QuickRefreshService>();
            services.AddSingleton<ContinuumArchiveService>();
            services.AddSingleton<VAL.Continuum.Pipeline.Inject.IContinuumInjectInbox, VAL.Continuum.Pipeline.Inject.ContinuumInjectInbox>();
            services.AddSingleton<TelemetryThresholdMonitor>();
            services.AddSingleton<IContinuumPump, ContinuumPump>();
            services.AddSingleton<SmokeTestRunner>();
        }

        private static void AddCommandContributor<TContributor>(IServiceCollection services)
            where TContributor : class, ICommandRegistryContributor
        {
            services.AddSingleton<TContributor>();
            services.AddSingleton<ICommandRegistryContributor>(sp => sp.GetRequiredService<TContributor>());
        }
    }
}
