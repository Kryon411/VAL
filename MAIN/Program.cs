using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VAL.Host.Services;
using VAL.Host.Services.Adapters;

namespace VAL
{
    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            IHost? host = null;

            try
            {
                host = Host.CreateDefaultBuilder()
                    .ConfigureServices(services =>
                    {
                        services.AddSingleton<IModuleLoader, ModuleLoaderAdapter>();
                        services.AddSingleton<ISessionContext, SessionContextAdapter>();
                        services.AddSingleton<IOperationCoordinator, OperationCoordinatorAdapter>();
                        services.AddSingleton<IToastService, ToastServiceAdapter>();
                        services.AddSingleton<ICommandDispatcher, CommandDispatcherAdapter>();
                        services.AddSingleton<MainWindow>();
                        services.AddSingleton<App>();
                    })
                    .Build();

                host.Start();

                var app = host.Services.GetRequiredService<App>();
                app.InitializeComponent();
                app.Run();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Application startup failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (host != null)
                {
                    try
                    {
                        host.StopAsync().GetAwaiter().GetResult();
                    }
                    catch
                    {
                        // Ignore shutdown errors.
                    }
                    finally
                    {
                        host.Dispose();
                    }
                }
            }
        }
    }
}
