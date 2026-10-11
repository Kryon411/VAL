using System;
using System.Collections.Generic;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using VAL.App.Host.Services;
using VAL.Host.Services;
using VAL.Host.Startup;

namespace VAL.App.Hosting
{
    public static class ValDesktopBootstrapper
    {
        public static int Run(string[]? args)
        {
            var context = ValDesktopBootstrapContext.Create(args);
            IHost? host = null;

            try
            {
                host = BuildHost(context);
                var wipe = new DataWipeService(host.Services.GetRequiredService<IAppPaths>(),
                    host.Services.GetRequiredService<VAL.Host.ILog>()).CompletePendingWipe();
                if (wipe is { Success: false })
                {
                    context.CrashGuard.MarkExpectedExit();
                    if (!context.SmokeTestSettings.Enabled)
                        host.Services.GetRequiredService<IDesktopDialogService>().ShowError(
                            "VAL data deletion is incomplete. VAL will not start capture or its browser until cleanup succeeds. " +
                            "Close other VAL instances and check folder permissions or links, then relaunch. " +
                            "The pending cleanup request has been kept.", "Delete VAL Data");
                    return 41;
                }
                return host.RunValDesktopApp(context.SafeBoot, context.StartupOptions, wipe);
            }
            catch (Exception ex)
            {
                context.SafeBoot.HandleFatalStartupException(ex);
                return Environment.ExitCode;
            }
            finally
            {
                host?.Dispose();
            }
        }

        internal static void ApplyCrashGuardSafeMode(
            StartupOptions startupOptions,
            bool crashGuardSafeMode)
        {
            ArgumentNullException.ThrowIfNull(startupOptions);

            if (!startupOptions.SafeModeExplicit && crashGuardSafeMode)
            {
                startupOptions.SafeMode = true;
            }
        }

        private static IHost BuildHost(ValDesktopBootstrapContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            // Fully-qualify Host to avoid accidentally binding to the VAL.Host namespace.
            return global::Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
                .UseContentRoot(AppPathLayout.ResolveContentRoot())
                .ConfigureAppConfiguration((_, config) =>
                {
                    ConfigureAppConfiguration(config, context.LocalConfigPath);
                    if (context.IsolatedDataRoot != null)
                    {
                        config.AddInMemoryCollection(new Dictionary<string, string?>
                        {
                            ["Val:DataRoot"] = context.IsolatedDataRoot,
                            ["Val:LogsPath"] = "Logs",
                            ["Val:ProfilePath"] = "Profile",
                        });
                    }
                })
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddValDesktopApp(
                        hostContext.Configuration,
                        context.StartupOptions,
                        context.SmokeTestSettings,
                        context.CrashGuard);
                })
                .Build();
        }

        private static void ConfigureAppConfiguration(
            IConfigurationBuilder configuration,
            string localConfigPath)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            configuration.AddEnvironmentVariables(prefix: "VAL__");
            configuration.AddJsonFile(localConfigPath, optional: true, reloadOnChange: false);
        }
    }
}
