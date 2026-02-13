using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VAL.Host.Commands;
using VAL.Host.Hosting;
using VAL.Host.Services;
using VAL.Host.Services.Adapters;
using VAL.Host.Startup;
using VAL.ViewModels;

namespace VAL.Host
{
    public static class ValHostComposition
    {
        public static IServiceCollection AddValHost(
            this IServiceCollection services,
            IConfiguration configuration,
            StartupOptions startupOptions,
            SmokeTestSettings smokeSettings,
            StartupCrashGuard crashGuard)
        {
            return ValHostServiceCollectionExtensions.AddValHost(services, configuration, builder =>
            {
                CommandRegistryFactory.RegisterCommands(
                    builder.CommandRegistry,
                    ContinuumCommandHandlers.HandleContinuumCommand,
                    VoidCommandHandlers.HandleSetEnabled,
                    PortalCommandHandlers.HandleSetEnabled,
                    PortalCommandHandlers.HandleOpenSnip,
                    PortalCommandHandlers.HandleSendStaged,
                    PrivacyCommandHandlers.HandleSetContinuumLogging,
                    PrivacyCommandHandlers.HandleSetPortalCapture,
                    PrivacyCommandHandlers.HandleOpenDataFolder,
                    PrivacyCommandHandlers.HandleWipeData,
                    ToolsCommandHandlers.HandleOpenTruthHealth,
                    ToolsCommandHandlers.HandleOpenDiagnostics,
                    NavigationCommandHandlers.HandleGoChat,
                    NavigationCommandHandlers.HandleGoBack,
                    DockCommandHandlers.HandleRequestModel,
                    DockCommandHandlers.HandleUiStateGet,
                    DockCommandHandlers.HandleUiStateSet,
                    AbyssCommandHandlers.HandleOpenQueryUi,
                    AbyssCommandHandlers.HandleSearch,
                    AbyssCommandHandlers.HandleRetryLast,
                    AbyssCommandHandlers.HandleInjectResult,
                    AbyssCommandHandlers.HandleInjectResults,
                    AbyssCommandHandlers.HandleLast,
                    AbyssCommandHandlers.HandleOpenSource,
                    AbyssCommandHandlers.HandleClearResults,
                    AbyssCommandHandlers.HandleDisregard,
                    AbyssCommandHandlers.HandleGetResults,
                    AbyssCommandHandlers.HandleInjectPrompt,
                    AbyssCommandHandlers.HandleInject);

                builder.Services.AddSingleton<ICommandDiagnosticsReporter, CommandDiagnosticsReporter>();

                builder.Services.AddSingleton<IModuleLoader, ModuleLoaderAdapter>();
                builder.Services.AddSingleton<ISessionContext, SessionContextAdapter>();
                builder.Services.AddSingleton<IOperationCoordinator, OperationCoordinatorAdapter>();
                builder.Services.AddSingleton<IToastService, ToastServiceAdapter>();
                builder.Services.AddSingleton<IToastHub, ToastHubAdapter>();
                builder.Services.AddSingleton<ICommandDispatcher, CommandDispatcherAdapter>();
                builder.Services.AddSingleton<ICrashHandler, CrashHandler>();
                builder.Services.AddSingleton<IPrivacySettingsService, PrivacySettingsService>();
                builder.Services.AddSingleton<IDataWipeService, DataWipeService>();
                builder.Services.AddSingleton<IDiagnosticsWindowService, DiagnosticsWindowService>();
                builder.Services.AddSingleton<ITruthHealthReportService, TruthHealthReportService>();
                builder.Services.AddSingleton<ITruthHealthWindowService, TruthHealthWindowService>();
                builder.Services.AddSingleton<IDockModelService, DockModelService>();
                builder.Services.AddSingleton<IDockUiStateStore, DockUiStateStore>();
                builder.Services.AddSingleton<IPortalRuntimeStateManager, PortalRuntimeStateManager>();
                builder.Services.AddSingleton<IPortalRuntimeService, PortalRuntimeService>();
                builder.Services.AddSingleton<IModuleRuntimeService, ModuleRuntimeService>();
                builder.Services.AddSingleton<VAL.Continuum.Pipeline.Truth.IContinuumWriter, VAL.Continuum.Pipeline.Truth.ContinuumWriter>();
                builder.Services.AddSingleton<VAL.Continuum.Pipeline.Inject.IContinuumInjectInbox, VAL.Continuum.Pipeline.Inject.ContinuumInjectInbox>();
                builder.Services.AddSingleton<IContinuumPump, ContinuumPump>();
                builder.Services.AddSingleton(smokeSettings);
                builder.Services.AddSingleton<SmokeTestState>();
                builder.Services.AddSingleton<SmokeTestRunner>();
                builder.Services.AddSingleton(startupOptions);
                builder.Services.AddSingleton(crashGuard);

                builder.Services.AddTransient<DiagnosticsViewModel>();
                builder.Services.AddTransient<DiagnosticsWindow>();
                builder.Services.AddTransient<UI.Truth.TruthHealthViewModel>();
                builder.Services.AddTransient<UI.Truth.TruthHealthWindow>();
                builder.Services.AddSingleton<MainWindowViewModel>();
                builder.Services.AddSingleton<MainWindow>();
                builder.Services.AddSingleton<App>();
            });
        }
    }
}
