using Microsoft.Extensions.DependencyInjection;

using VAL.Host.Services;

namespace VAL.App.Hosting
{
    public static class ValAppServiceCollectionExtensions
    {
        public static IServiceCollection AddValAppShell(this IServiceCollection services)
        {
            services.AddSingleton<IControlCentreUiStateStore, ControlCentreUiStateStore>();
            services.AddSingleton<IDesktopDialogService, MessageBoxDesktopDialogService>();
            services.AddSingleton<IDeferredActionFactory, DispatcherDeferredActionFactory>();
            services.AddSingleton<MainWindowShellStateController>();
            services.AddSingleton<MainWindowShellBridgeController>();
            services.AddSingleton<MainWindowShellTimingController>();
            services.AddSingleton<ControlCentreOverlayHost>();
            services.AddSingleton<IControlCentreOverlayHost>(sp => sp.GetRequiredService<ControlCentreOverlayHost>());
            services.AddSingleton<MainWindowOverlayController>();
            services.AddSingleton<MainWindowNativeChromeController>();
            services.AddSingleton<MainWindowStartupCoordinator>();
            services.AddTransient(typeof(IWindowFactory<>), typeof(ServiceProviderWindowFactory<>));
            services.AddTransient(typeof(IWindowFactory<,>), typeof(ServiceProviderWindowFactory<,>));
            services.AddTransient<ControlCentreOverlay>();
            services.AddTransient<CrashWindowViewModel>();
            services.AddTransient<CrashWindow>();
            services.AddTransient<DiagnosticsViewModel>();
            services.AddTransient<DiagnosticsWindow>();
            services.AddTransient<TruthHealthViewModel>();
            services.AddTransient<TruthHealthWindow>();
            services.AddSingleton<MainWindowViewModel>();
            services.AddSingleton<MainWindow>();
            services.AddSingleton<ValApplication>();

            return services;
        }
    }
}
