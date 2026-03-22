using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using VAL.Host.Options;
using VAL.Host.Services;
using VAL.Host.Startup;
using VAL.Hosting;

namespace VAL
{
    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            IHost? host = null;
            var smokeSettings = SmokeTestSettings.FromArgs(args);
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
                        services.AddValDesktopApp(context.Configuration, startupOptions, smokeSettings, crashGuard);
                    })
                    .Build();

                Environment.ExitCode = host.RunValDesktopApp(safeBoot, startupOptions);
            }
            catch (Exception ex)
            {
                safeBoot.HandleFatalStartupException(ex);
            }
            finally
            {
                if (host != null)
                {
                    host.Dispose();
                }
            }
        }
    }
}
