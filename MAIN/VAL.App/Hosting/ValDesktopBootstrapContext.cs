using System;
using System.IO;

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
            SafeBoot safeBoot,
            string? isolatedDataRoot)
        {
            LocalConfigPath = localConfigPath;
            SmokeTestSettings = smokeSettings;
            StartupOptions = startupOptions;
            CrashGuard = crashGuard;
            SafeBoot = safeBoot;
            IsolatedDataRoot = isolatedDataRoot;
        }

        public string LocalConfigPath { get; }
        public SmokeTestSettings SmokeTestSettings { get; }
        public StartupOptions StartupOptions { get; }
        public StartupCrashGuard CrashGuard { get; }
        public SafeBoot SafeBoot { get; }
        public string? IsolatedDataRoot { get; }

        public static ValDesktopBootstrapContext Create(string[]? args)
        {
            var rawArgs = args ?? Array.Empty<string>();
            var smokeSettings = SmokeTestSettings.FromArgs(rawArgs);
            var validationSettings = ValidationSessionSettings.FromArgs(rawArgs);
            if (smokeSettings.Enabled && validationSettings.Enabled)
                throw new ArgumentException("Smoke and interactive validation modes cannot run together.");
            var startupOptions = StartupOptionsParser.Parse(rawArgs);
            startupOptions.ValidationSession = validationSettings.Enabled;
            var isolatedDataRoot = smokeSettings.Enabled
                ? ResolveSmokeDataRoot(smokeSettings.DataRoot)
                : validationSettings.Enabled ? ResolveValidationDataRoot(validationSettings.DataRoot) : null;
            var logBootstrapper = new ValLogBootstrapper();
            var crashGuard = new StartupCrashGuard(logBootstrapper, isolatedDataRoot);
            ValDesktopBootstrapper.ApplyCrashGuardSafeMode(
                startupOptions,
                crashGuard.EvaluateAndMarkStarting());

            var localConfigPath = isolatedDataRoot == null
                ? ValOptions.DefaultLocalConfigPath
                : Path.Combine(isolatedDataRoot, "config.json");
            var dialogService = new MessageBoxDesktopDialogService();
            var safeBoot = new SafeBoot(localConfigPath, smokeSettings, logBootstrapper, dialogService);

            return new ValDesktopBootstrapContext(
                localConfigPath,
                smokeSettings,
                startupOptions,
                crashGuard,
                safeBoot,
                isolatedDataRoot);
        }

        internal static string ResolveSmokeDataRoot(string? requestedRoot) => ResolveIsolatedDataRoot(requestedRoot, "VAL.Smoke");

        internal static string ResolveValidationDataRoot(string? requestedRoot) => ResolveIsolatedDataRoot(requestedRoot, "VAL.Validation");

        private static string ResolveIsolatedDataRoot(string? requestedRoot, string folder)
        {
            var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), folder));
            var root = Path.GetFullPath(string.IsNullOrWhiteSpace(requestedRoot)
                ? Path.Combine(parent, Guid.NewGuid().ToString("N")) : requestedRoot);
            if (!string.Equals(Path.GetDirectoryName(root), parent, StringComparison.OrdinalIgnoreCase) ||
                !Guid.TryParseExact(Path.GetFileName(root), "N", out _))
                throw new ArgumentException($"Isolated data must use a GUID-named folder directly under Temp\\{folder}.");
            for (var current = new DirectoryInfo(root); current != null; current = current.Parent)
                if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new ArgumentException("Isolated data paths must not contain directory links.");
            return root;
        }
    }
}
