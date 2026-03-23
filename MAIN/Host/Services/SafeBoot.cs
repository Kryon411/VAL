using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using VAL.Host.Logging;
using VAL.Host.Options;

namespace VAL.Host.Services
{
    public sealed class SafeBoot
    {
        private const string Category = "SafeBoot";
        private readonly string _localConfigPath;
        private readonly SmokeTestSettings _smokeSettings;
        private readonly ILogBootstrapper _log;

        public SafeBoot(string localConfigPath, SmokeTestSettings smokeSettings, ILogBootstrapper log)
        {
            _localConfigPath = localConfigPath;
            _smokeSettings = smokeSettings ?? throw new ArgumentNullException(nameof(smokeSettings));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public bool FallbackUsed { get; private set; }

        public void LogStartupInfo(IBuildInfo buildInfo, IAppPaths appPaths, WebViewOptions webViewOptions)
        {
            InitializeDiagnostics(buildInfo, appPaths);
            EnsureDirectory(appPaths.DataRoot);
            EnsureDirectory(appPaths.LogsRoot);
            EnsureDirectory(appPaths.ProfileRoot);
            EnsureDirectory(appPaths.ModulesRoot);

            _log.Info(Category, $"Version: {buildInfo.Version}");
            _log.Info(Category, $"InformationalVersion: {buildInfo.InformationalVersion}");
            _log.Info(Category, $"Environment: {buildInfo.Environment}");
            if (!string.IsNullOrWhiteSpace(buildInfo.GitSha))
                _log.Info(Category, $"GitSha: {buildInfo.GitSha}");
            if (!string.IsNullOrWhiteSpace(buildInfo.BuildDate))
                _log.Info(Category, $"BuildDateUtc: {buildInfo.BuildDate}");

            var appSettingsPath = Path.Combine(appPaths.ContentRoot, "appsettings.json");
            _log.Info(Category, $"ContentRoot: {appPaths.ContentRoot}");
            _log.Info(Category, $"ConfigPath.AppSettings: {appSettingsPath}");
            _log.Info(Category, $"ConfigPath.LocalOverride: {_localConfigPath}");
            _log.Info(Category, $"DevToolsEnabled: {webViewOptions.EffectiveAllowDevTools}");
            _log.Info(Category, $"SafeBootFallbackUsed: {FallbackUsed}");
        }

        public void HandleFatalStartupException(Exception exception)
        {
            FallbackUsed = true;

            try
            {
                _log.Warn(Category, "SafeBoot fallback invoked due to startup failure.");
                _log.LogError(Category, exception.ToString());
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

        private void InitializeDiagnostics(IBuildInfo buildInfo, IAppPaths appPaths)
        {
            try
            {
                if (appPaths == null || string.IsNullOrWhiteSpace(appPaths.LogsRoot))
                    return;

                Directory.CreateDirectory(appPaths.LogsRoot);
                var logPath = Path.Combine(appPaths.LogsRoot, "VAL.log");
                _log.AddSink(new RollingFileLogSink(logPath));

                var version = buildInfo?.Version ?? "unknown";
                var hash = buildInfo?.InformationalVersion ?? "unknown";
                var os = RuntimeInformation.OSDescription ?? "unknown";
                var rid = RuntimeInformation.RuntimeIdentifier ?? "unknown";
                _log.Info("Startup", $"VAL start v={version} hash={hash} os={os} rid={rid}");
            }
            catch
            {
                // Logging must never throw.
            }
        }
    }
}
