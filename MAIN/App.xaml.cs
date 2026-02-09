using System;
using System.IO;
using System.Windows;
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

namespace VAL;

/// <summary>
/// Code-only WPF Application shell.
///
/// Phase 1 now uses App.OnStartup as the single entry point (Host + DI composition root).
/// In that model, App.xaml is optional; we intentionally do NOT call InitializeComponent here
/// so the app does not depend on XAML-generated code-behind.
/// </summary>
public partial class App : Application
{
    private static readonly Uri ThemeDictionaryUri =
        new("pack://application:,,,/VAL;component/UI/VALWindowTheme.xaml", UriKind.Absolute);

    private IHost? _host;
    private SmokeTestSettings? _smokeSettings;
    private SmokeTestState? _smokeState;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        EnsureThemeLoaded();

        var args = e.Args ?? Array.Empty<string>();
        _smokeSettings = SmokeTestSettings.FromArgs(args);
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
        var safeBoot = new SafeBoot(localConfigPath, _smokeSettings);

        try
        {
            _host = global::Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
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
                    services.AddSingleton(_smokeSettings);
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
                })
                .Build();

            ValHostServices.Initialize(_host.Services);
            _host.Start();

            var appPaths = _host.Services.GetRequiredService<IAppPaths>();
            var buildInfo = _host.Services.GetRequiredService<IBuildInfo>();
            var webViewOptions = _host.Services.GetRequiredService<IOptions<WebViewOptions>>().Value;
            safeBoot.LogStartupInfo(buildInfo, appPaths, webViewOptions);
            if (startupOptions.SafeMode)
            {
                ValLog.Info("Startup", "SAFE MODE: modules disabled");
            }

            var crashHandler = _host.Services.GetRequiredService<ICrashHandler>();
            crashHandler.Register(this);

            if (_smokeSettings.Enabled)
            {
                var smokeRunner = _host.Services.GetRequiredService<SmokeTestRunner>();
                _smokeState = _host.Services.GetRequiredService<SmokeTestState>();
                smokeRunner.Register(this, _smokeState);
            }

            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            MainWindow = mainWindow;
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            safeBoot.HandleFatalStartupException(ex);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        base.OnExit(e);

        if (_smokeSettings?.Enabled == true && _smokeState != null)
        {
            Environment.ExitCode = _smokeState.Completion.Task.GetAwaiter().GetResult();
        }

        if (_host != null)
        {
            try
            {
                _host.StopAsync().GetAwaiter().GetResult();
            }
            catch
            {
                // Ignore shutdown errors.
            }
            finally
            {
                _host.Dispose();
                _host = null;
            }
        }
    }

    private static void EnsureThemeLoaded()
    {
        var resources = Current?.Resources;
        if (resources is null)
        {
            return;
        }

        foreach (var dictionary in resources.MergedDictionaries)
        {
            if (dictionary.Source is null)
            {
                continue;
            }

            if (dictionary.Source == ThemeDictionaryUri)
            {
                return;
            }
        }

        resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = ThemeDictionaryUri
        });
    }
}
