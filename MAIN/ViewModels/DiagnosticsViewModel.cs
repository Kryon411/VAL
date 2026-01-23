using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Web.WebView2.Core;
using VAL.Host.Options;
using VAL.Host.Services;

namespace VAL.ViewModels
{
    public sealed class DiagnosticsViewModel
    {
        private readonly IProcessLauncher _processLauncher;
        private readonly IAppPaths _appPaths;

        public DiagnosticsViewModel(
            IBuildInfo buildInfo,
            IAppPaths appPaths,
            IProcessLauncher processLauncher,
            IOptions<WebViewOptions> webViewOptions,
            IOptions<ModuleOptions> moduleOptions,
            IHostEnvironment hostEnvironment)
        {
            _processLauncher = processLauncher;
            _appPaths = appPaths;

            DiagnosticsText = BuildDiagnosticsText(buildInfo, webViewOptions.Value, moduleOptions.Value, hostEnvironment, appPaths);

            CopyDiagnosticsCommand = new RelayCommand(CopyDiagnostics);
            OpenLogsFolderCommand = new RelayCommand(() => _processLauncher.OpenFolder(_appPaths.LogsRoot));
            OpenDataFolderCommand = new RelayCommand(() => _processLauncher.OpenFolder(_appPaths.DataRoot));
            OpenModulesFolderCommand = new RelayCommand(() => _processLauncher.OpenFolder(_appPaths.ModulesRoot));
            OpenProfileFolderCommand = new RelayCommand(() => _processLauncher.OpenFolder(_appPaths.ProfileRoot));
        }

        public string DiagnosticsText { get; }
        public RelayCommand CopyDiagnosticsCommand { get; }
        public RelayCommand OpenLogsFolderCommand { get; }
        public RelayCommand OpenDataFolderCommand { get; }
        public RelayCommand OpenModulesFolderCommand { get; }
        public RelayCommand OpenProfileFolderCommand { get; }

        private void CopyDiagnostics()
        {
            try
            {
                Clipboard.SetText(DiagnosticsText);
            }
            catch
            {
                // Clipboard failures should not crash diagnostics.
            }
        }

        private static string BuildDiagnosticsText(
            IBuildInfo buildInfo,
            WebViewOptions webViewOptions,
            ModuleOptions moduleOptions,
            IHostEnvironment hostEnvironment,
            IAppPaths appPaths)
        {
            var builder = new StringBuilder();

            builder.AppendLine("VAL Diagnostics");
            builder.AppendLine("-------------------------------");
            builder.AppendLine($"Version: {buildInfo.Version}");
            builder.AppendLine($"Informational Version: {buildInfo.InformationalVersion}");
            builder.AppendLine($"Environment: {buildInfo.Environment}");
            if (!string.IsNullOrWhiteSpace(buildInfo.GitSha))
                builder.AppendLine($"Git SHA: {buildInfo.GitSha}");
            if (!string.IsNullOrWhiteSpace(buildInfo.BuildDate))
                builder.AppendLine($"Build Date (UTC): {buildInfo.BuildDate}");

            builder.AppendLine();
            builder.AppendLine("Runtime");
            builder.AppendLine("-------------------------------");
            builder.AppendLine($"OS: {RuntimeInformation.OSDescription}");
            builder.AppendLine($"Framework: {RuntimeInformation.FrameworkDescription}");
            builder.AppendLine($"Process Arch: {RuntimeInformation.ProcessArchitecture}");

            builder.AppendLine();
            builder.AppendLine("WebView");
            builder.AppendLine("-------------------------------");
            builder.AppendLine($"StartUrl: {webViewOptions.StartUrl}");
            builder.AppendLine($"AllowDevTools: {webViewOptions.AllowDevTools}");
            builder.AppendLine($"BlockNewWindow: {webViewOptions.BlockNewWindow}");
            builder.AppendLine($"UserAgentOverride: {(string.IsNullOrWhiteSpace(webViewOptions.UserAgentOverride) ? "(none)" : webViewOptions.UserAgentOverride)}");
            builder.AppendLine($"WebView2 Runtime: {GetWebView2Version()}");

            builder.AppendLine();
            builder.AppendLine("Paths");
            builder.AppendLine("-------------------------------");
            builder.AppendLine($"DataRoot: {appPaths.DataRoot}");
            builder.AppendLine($"LogsRoot: {appPaths.LogsRoot}");
            builder.AppendLine($"ModulesRoot: {appPaths.ModulesRoot}");
            builder.AppendLine($"ProfileRoot: {appPaths.ProfileRoot}");

            builder.AppendLine();
            builder.AppendLine("Modules");
            builder.AppendLine("-------------------------------");
            if (moduleOptions.EnabledModules.Length == 0)
            {
                builder.AppendLine("Enabled: (none)");
            }
            else
            {
                builder.AppendLine("Enabled:");
                foreach (var module in moduleOptions.EnabledModules)
                {
                    builder.AppendLine($"- {module}");
                }
            }

            builder.AppendLine();
            builder.AppendLine("Configuration");
            builder.AppendLine("-------------------------------");
            builder.AppendLine("Sources:");
            builder.AppendLine("- appsettings.json");
            if (!string.IsNullOrWhiteSpace(hostEnvironment.EnvironmentName))
                builder.AppendLine($"- appsettings.{hostEnvironment.EnvironmentName}.json");
            builder.AppendLine($"- local override: {ResolveLocalConfigPath()}");
            builder.AppendLine("- environment variables: prefix VAL__");

            return builder.ToString();
        }

        private static string GetWebView2Version()
        {
            try
            {
                return CoreWebView2Environment.GetAvailableBrowserVersionString();
            }
            catch
            {
                return "Unavailable";
            }
        }

        private static string ResolveLocalConfigPath()
        {
            return System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VAL",
                "config.json");
        }
    }
}
