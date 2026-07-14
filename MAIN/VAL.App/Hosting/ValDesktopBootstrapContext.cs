using System;

using VAL.App.Host.Services;
using VAL.App.Host.Startup;
using VAL.Host.Logging;
using VAL.Host.Options;
using VAL.Host.Services;
using VAL.Host.Startup;

namespace VAL.App.Hosting
{
    internal sealed class ValDesktopBootstrapContext
    {
        private ValDesktopBootstrapContext(
            string localConfigPath,
            SmokeTestSettings smokeSettings,
            StartupOptions startupOptions,
            StartupCrashGuard crashGuard,
            SafeBoot safeBoot)
        {
            LocalConfigPath = localConfigPath;
            SmokeTestSettings = smokeSettings;
            StartupOptions = startupOptions;
            CrashGuard = crashGuard;
            SafeBoot = safeBoot;
        }

        public string LocalConfigPath { get; }
        public SmokeTestSettings SmokeTestSettings { get; }
        public StartupOptions StartupOptions { get; }
        public StartupCrashGuard CrashGuard { get; }
        public SafeBoot SafeBoot { get; }

        public static ValDesktopBootstrapContext Create(string[]? args)
        {
            var rawArgs = args ?? Array.Empty<string>();
            var smokeSettings = SmokeTestSettings.FromArgs(rawArgs);
            var startupOptions = StartupOptionsParser.Parse(rawArgs);
            var logBootstrapper = new ValLogBootstrapper();
            var crashGuard = new StartupCrashGuard(logBootstrapper);
            ValDesktopBootstrapper.ApplyCrashGuardSafeMode(
                startupOptions,
                crashGuard.EvaluateAndMarkStarting());

            var localConfigPath = ValOptions.DefaultLocalConfigPath;
            var dialogService = new MessageBoxDesktopDialogService();
            var safeBoot = new SafeBoot(localConfigPath, smokeSettings, logBootstrapper, dialogService);

            return new ValDesktopBootstrapContext(
                localConfigPath,
                smokeSettings,
                startupOptions,
                crashGuard,
                safeBoot);
        }
    }
}
