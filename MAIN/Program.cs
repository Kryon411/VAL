using System;
using System.IO;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VAL.Host.Options;
using VAL.Host.Services;
using VAL.Host.Services.Adapters;
using VAL.Host.WebMessaging;
using VAL.ViewModels;

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
                    .ConfigureAppConfiguration((context, config) =>
                    {
                        var localConfigPath = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "VAL",
                            "config.json");

                        config.AddEnvironmentVariables(prefix: "VAL__");
                        config.AddJsonFile(localConfigPath, optional: true, reloadOnChange: false);
                    })
                    .ConfigureServices((context, services) =>
                    {
                        services.AddOptions<ValOptions>()
                            .Bind(context.Configuration.GetSection(ValOptions.SectionName))
                            .PostConfigure(options => options.ApplyDefaults())
                            .Validate(options => !string.IsNullOrWhiteSpace(options.DataRoot), "Val options must include a data root.");

                        services.AddOptions<WebViewOptions>()
                            .Bind(context.Configuration.GetSection(WebViewOptions.SectionName))
                            .PostConfigure(options => options.ApplyDefaults())
                            .Validate(options => !string.IsNullOrWhiteSpace(options.StartUrl), "WebView options must include a start URL.");

                        services.AddOptions<ModuleOptions>()
                            .Bind(context.Configuration.GetSection(ModuleOptions.SectionName))
                            .PostConfigure(options => options.ApplyDefaults())
                            .Validate(options => options.EnabledModules != null, "Module options must include module list.");

                        services.AddSingleton<IModuleLoader, ModuleLoaderAdapter>();
                        services.AddSingleton<ISessionContext, SessionContextAdapter>();
                        services.AddSingleton<IOperationCoordinator, OperationCoordinatorAdapter>();
                        services.AddSingleton<IToastService, ToastServiceAdapter>();
                        services.AddSingleton<ICommandDispatcher, CommandDispatcherAdapter>();
                        services.AddSingleton<IAppPaths, AppPaths>();
                        services.AddSingleton<IProcessLauncher, ProcessLauncher>();
                        services.AddSingleton<IUiThread, UiThread>();
                        services.AddSingleton<IPortalRuntimeService, PortalRuntimeService>();
                        services.AddSingleton<IModuleRuntimeService, ModuleRuntimeService>();
                        services.AddSingleton<IContinuumPump, ContinuumPump>();
                        services.AddSingleton<IWebViewRuntime, WebViewRuntime>();
                        services.AddSingleton<IWebMessageSender, WebMessageSender>();

                        services.AddSingleton<MainWindowViewModel>();
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
