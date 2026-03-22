using Microsoft.Extensions.DependencyInjection;
using VAL.Host.Services;
using VAL.UI.Truth;
using VAL.ViewModels;

namespace VAL.Hosting
{
    public static class ValAppServiceCollectionExtensions
    {
        public static IServiceCollection AddValAppShell(this IServiceCollection services)
        {
            services.AddSingleton<global::VAL.IControlCentreUiStateStore, global::VAL.ControlCentreUiStateStore>();
            services.AddTransient(typeof(IWindowFactory<>), typeof(ServiceProviderWindowFactory<>));
            services.AddTransient<DiagnosticsViewModel>();
            services.AddTransient<DiagnosticsWindow>();
            services.AddTransient<TruthHealthViewModel>();
            services.AddTransient<TruthHealthWindow>();
            services.AddSingleton<MainWindowViewModel>();
            services.AddSingleton<MainWindow>();
            services.AddSingleton<App>();

            return services;
        }
    }
}
