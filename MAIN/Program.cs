using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using VAL.Host;
using VAL.Host.Options;
using VAL.Host.Services;
using VAL.Host.Services.Adapters;
using VAL.Host.Startup;
using VAL.Host.WebMessaging;
using VAL.UI.Truth;
using VAL.ViewModels;

namespace VAL
{
    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            IHost? host = null;
            var smokeSettings = SmokeTestSettings.FromArgs(args);
            SmokeTestState? smokeState = null;
            var startupOptions = StartupOptionsParser.Parse(args);
            var crashGuard = new StartupCrashGuard();
            var crashGuardSafeMode = crashGuard.EvaluateAndMarkStarting();
            if (!startupOptions.SafeModeExplicit && crashGuardSafeMode)
            {
                startupOptions.SafeMode = true;
            }

            var localConfigPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VAL",
                "config.json");
            var safeBoot = new SafeBoot(localConfigPath, smokeSettings);

            try
            {
                // Fully-qualify Host to avoid accidentally binding to the VAL.Host namespace.
                host = global::Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
                    .ConfigureAppConfiguration((context, config) =>
                    {
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
                        services.AddSingleton<IBuildInfo, BuildInfo>();
                        services.AddSingleton<ICrashHandler, CrashHandler>();
                        services.AddSingleton<IDiagnosticsWindowService, DiagnosticsWindowService>();
                        services.AddSingleton<ITruthHealthService, TruthHealthService>();
                        services.AddSingleton<ITruthHealthWindowService, TruthHealthWindowService>();
                        services.AddSingleton<IPortalRuntimeService, PortalRuntimeService>();
                        services.AddSingleton<IModuleRuntimeService, ModuleRuntimeService>();
                        services.AddSingleton<IContinuumPump, ContinuumPump>();
                        services.AddSingleton<IWebViewRuntime, WebViewRuntime>();
                        services.AddSingleton<IWebMessageSender, WebMessageSender>();
                        services.AddSingleton(smokeSettings);
                        services.AddSingleton<SmokeTestState>();
                        services.AddSingleton<SmokeTestRunner>();
                        services.AddSingleton(startupOptions);
                        services.AddSingleton(crashGuard);

                        services.AddTransient<DiagnosticsViewModel>();
                        services.AddTransient<DiagnosticsWindow>();
                        services.AddTransient<TruthHealthViewModel>();
                        services.AddTransient<TruthHealthWindow>();
                        services.AddSingleton<MainWindowViewModel>();
                        services.AddSingleton<MainWindow>();
                        services.AddSingleton<App>();
                    })
                    .Build();

                host.Start();

                var appPaths = host.Services.GetRequiredService<IAppPaths>();
                var buildInfo = host.Services.GetRequiredService<IBuildInfo>();
                var webViewOptions = host.Services.GetRequiredService<IOptions<WebViewOptions>>().Value;
                safeBoot.LogStartupInfo(buildInfo, appPaths, webViewOptions);
                if (startupOptions.SafeMode)
                {
                    ValLog.Info("Startup", "SAFE MODE: modules disabled");
                }

                // App is code-only (no InitializeComponent). MainWindow is created in App.OnStartup.
                var app = host.Services.GetRequiredService<App>();
                var crashHandler = host.Services.GetRequiredService<ICrashHandler>();
                crashHandler.Register(app);

                if (smokeSettings.Enabled)
                {
                    var smokeRunner = host.Services.GetRequiredService<SmokeTestRunner>();
                    smokeState = host.Services.GetRequiredService<SmokeTestState>();
                    smokeRunner.Register(app, smokeState);
                }

                app.Run();

                if (smokeSettings.Enabled && smokeState != null)
                {
                    Environment.ExitCode = smokeState.Completion.Task.GetAwaiter().GetResult();
                }
            }
            catch (Exception ex)
            {
                safeBoot.HandleFatalStartupException(ex);
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
