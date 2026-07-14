using System;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

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
                return host.RunValDesktopApp(context.SafeBoot, context.StartupOptions);
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
                .ConfigureAppConfiguration((_, config) =>
                {
                    ConfigureAppConfiguration(config, context.LocalConfigPath);
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
