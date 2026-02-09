using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using VAL.App.Services;
using VAL.Host;
using VAL.Host.Options;
using VAL.Host.Services;
using VAL.Host.Services.Adapters;
using VAL.Host.Startup;
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
                        services.AddValHost(context.Configuration);
                        services.AddSingleton<ICommandDispatcher, CommandDispatcherAdapter>();
                        services.AddSingleton<IToastService, ToastServiceAdapter>();
                        services.AddSingleton<IToastHub, ToastHubAdapter>();
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
                        services.AddSingleton(smokeSettings);
                        services.AddSingleton<SmokeTestState>();
                        services.AddSingleton<SmokeTestRunner>();
                        services.AddSingleton(startupOptions);
                        services.AddSingleton(crashGuard);
                        services.AddSingleton<ICrashHandler, CrashHandler>();
                        services.AddSingleton<IDiagnosticsWindowService, DiagnosticsWindowService>();
                        services.AddSingleton<ITruthHealthWindowService, TruthHealthWindowService>();

                        services.AddTransient<DiagnosticsViewModel>();
                        services.AddTransient<DiagnosticsWindow>();
                        services.AddTransient<UI.Truth.TruthHealthViewModel>();
                        services.AddTransient<UI.Truth.TruthHealthWindow>();
                        services.AddSingleton<MainWindowViewModel>();
                        services.AddSingleton<MainWindow>();
                        services.AddSingleton<App>();
                    })
                    .Build();

                ValHostServices.Initialize(host.Services);
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
