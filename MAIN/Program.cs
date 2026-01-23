using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VAL.Host.Services;
using VAL.Host.Services.Adapters;
using VAL.Host.WebMessaging;

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
                // Fully-qualify Host to avoid accidentally binding to the VAL.Host namespace.
                host = global::Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
                    .ConfigureServices(services =>
                    {
                        services.AddSingleton<IModuleLoader, ModuleLoaderAdapter>();
                        services.AddSingleton<ISessionContext, SessionContextAdapter>();
                        services.AddSingleton<IOperationCoordinator, OperationCoordinatorAdapter>();
                        services.AddSingleton<IToastService, ToastServiceAdapter>();
                        services.AddSingleton<ICommandDispatcher, CommandDispatcherAdapter>();
                        services.AddSingleton<IWebViewRuntime, WebViewRuntime>();
                        services.AddSingleton<IWebMessageSender, WebMessageSender>();

                        services.AddSingleton<MainWindow>();
                        services.AddSingleton<App>();
                    })
                    .Build();

                host.Start();

                // App is code-only (no InitializeComponent). MainWindow is created in App.OnStartup.
                var app = host.Services.GetRequiredService<App>();
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
