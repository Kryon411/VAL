using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using VAL.Continuum.Pipeline.Common;
using VAL.Host.Logging;
using VAL.Host.Options;

namespace VAL.Host.Services
{
    public sealed class SafeBoot
    {
        private const string Category = "SafeBoot";
        private readonly string _localConfigPath;
        private readonly SmokeTestSettings _smokeSettings;

        public SafeBoot(string localConfigPath, SmokeTestSettings smokeSettings)
        {
            _localConfigPath = localConfigPath;
            _smokeSettings = smokeSettings;
        }

        public bool FallbackUsed { get; private set; }

        public void LogStartupInfo(IBuildInfo buildInfo, IAppPaths appPaths, WebViewOptions webViewOptions)
        {
            InitializeDiagnostics(buildInfo);
            EnsureDirectory(appPaths.DataRoot);
            EnsureDirectory(appPaths.LogsRoot);
            EnsureDirectory(appPaths.ProfileRoot);
            EnsureDirectory(appPaths.ModulesRoot);

            ValLog.Info(Category, $"Version: {buildInfo.Version}");
            ValLog.Info(Category, $"InformationalVersion: {buildInfo.InformationalVersion}");
            ValLog.Info(Category, $"Environment: {buildInfo.Environment}");
            if (!string.IsNullOrWhiteSpace(buildInfo.GitSha))
                ValLog.Info(Category, $"GitSha: {buildInfo.GitSha}");
            if (!string.IsNullOrWhiteSpace(buildInfo.BuildDate))
                ValLog.Info(Category, $"BuildDateUtc: {buildInfo.BuildDate}");

            var appSettingsPath = Path.Combine(appPaths.ContentRoot, "appsettings.json");
            ValLog.Info(Category, $"ContentRoot: {appPaths.ContentRoot}");
            ValLog.Info(Category, $"ConfigPath.AppSettings: {appSettingsPath}");
            ValLog.Info(Category, $"ConfigPath.LocalOverride: {_localConfigPath}");
            ValLog.Info(Category, $"DevToolsEnabled: {webViewOptions.EffectiveAllowDevTools}");
            ValLog.Info(Category, $"SafeBootFallbackUsed: {FallbackUsed}");
        }

        public void HandleFatalStartupException(Exception exception)
        {
            FallbackUsed = true;

            try
            {
                ValLog.Warn(Category, "SafeBoot fallback invoked due to startup failure.");
                ValLog.Error(Category, exception.ToString());
            }
            catch
            {
                // Logging must never throw.
            }

#if DEBUG
            if (_smokeSettings.Enabled)
            {
                Environment.ExitCode = 40;
                return;
            }

            MessageBox.Show($"Application startup failed: {exception.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
#else
            if (_smokeSettings.Enabled)
            {
                Environment.ExitCode = 40;
                return;
            }

            try
            {
                MessageBox.Show("VAL failed to start. Please check the logs for details.", "VAL", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch
            {
                // Avoid secondary failures during shutdown.
            }

            Environment.ExitCode = 1;
#endif
        }

        private static void EnsureDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            try
            {
                Directory.CreateDirectory(path);
            }
            catch
            {
                // Startup should never fail due to directory creation.
            }
        }

        private static void InitializeDiagnostics(IBuildInfo buildInfo)
        {
            try
            {
                var productRoot = ContinuumContext.ResolveProductRoot();
                if (string.IsNullOrWhiteSpace(productRoot))
                    return;

                var logsRoot = Path.Combine(productRoot, "Logs");
                Directory.CreateDirectory(logsRoot);
                var logPath = Path.Combine(logsRoot, "VAL.log");
                ValLog.AddSink(new RollingFileLogSink(logPath));

                var version = buildInfo?.Version ?? "unknown";
                var hash = buildInfo?.InformationalVersion ?? "unknown";
                var os = RuntimeInformation.OSDescription ?? "unknown";
                var rid = RuntimeInformation.RuntimeIdentifier ?? "unknown";
                ValLog.Info("Startup", $"VAL start v={version} hash={hash} os={os} rid={rid}");
            }
            catch
            {
                // Logging must never throw.
            }
        }
    }
}
