using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Options;
using VAL.Host.Options;

namespace VAL.Host.Services
{
    public sealed class AppPaths : IAppPaths
    {
        public string ContentRoot { get; }
        public string DataRoot { get; }
        public string LogsRoot { get; }
        public string ModulesRoot { get; }
        public string ProfileRoot { get; }

        public AppPaths(IOptions<ValOptions> options)
        {
            var warnings = new List<string>();
            var config = options.Value;

            ContentRoot = ResolveContentRoot(warnings);
            var defaultDataRoot = ValOptions.DefaultDataRoot;
            var defaultModulesRoot = Path.Combine(ContentRoot, "Modules");

            DataRoot = NormalizePath(config.DataRoot, defaultDataRoot, "DataRoot", warnings, defaultDataRoot);
            LogsRoot = NormalizePath(config.LogsPath, Path.Combine(DataRoot, ValOptions.DefaultLogsPath), "LogsPath", warnings, DataRoot);
            ProfileRoot = NormalizePath(config.ProfilePath, Path.Combine(DataRoot, ValOptions.DefaultProfilePath), "ProfilePath", warnings, DataRoot);
            ModulesRoot = NormalizePath(config.ModulesPath, defaultModulesRoot, "ModulesPath", warnings, ContentRoot);

            var logFile = Path.Combine(LogsRoot, "VAL.log");
            ValLog.Configure(logFile, config.EnableVerboseLogging);

            ValLog.Verbose(nameof(AppPaths), $"Resolved ContentRoot: {ContentRoot}");
            ValLog.Verbose(nameof(AppPaths), $"Resolved DataRoot: {DataRoot}");
            ValLog.Verbose(nameof(AppPaths), $"Resolved LogsRoot: {LogsRoot}");
            ValLog.Verbose(nameof(AppPaths), $"Resolved ProfileRoot: {ProfileRoot}");
            ValLog.Verbose(nameof(AppPaths), $"Resolved ModulesRoot: {ModulesRoot}");

            foreach (var warning in warnings)
            {
                ValLog.Warn(nameof(AppPaths), warning);
            }
        }

        private static string ResolveContentRoot(List<string> warnings)
        {
            try
            {
                var exePath = Environment.ProcessPath;
                var exeDir = string.IsNullOrWhiteSpace(exePath) ? null : Path.GetDirectoryName(exePath);
                var candidate = exeDir ?? AppContext.BaseDirectory;
                return Path.GetFullPath(candidate);
            }
            catch
            {
                warnings.Add("Failed to resolve content root from executable path. Falling back to AppContext.BaseDirectory.");
                try
                {
                    return Path.GetFullPath(AppContext.BaseDirectory);
                }
                catch
                {
                    return AppContext.BaseDirectory;
                }
            }
        }

        private static string NormalizePath(string? value, string fallback, string label, List<string> warnings, string? basePath)
        {
            var candidate = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
            candidate = Environment.ExpandEnvironmentVariables(candidate);

            if (!Path.IsPathRooted(candidate) && !string.IsNullOrWhiteSpace(basePath))
            {
                candidate = Path.Combine(basePath, candidate);
            }

            try
            {
                return Path.GetFullPath(candidate);
            }
            catch
            {
                warnings.Add($"Invalid path for {label}. Falling back to {fallback}.");
                try
                {
                    return Path.GetFullPath(fallback);
                }
                catch
                {
                    warnings.Add($"Fallback path for {label} is invalid: {fallback}.");
                    return fallback;
                }
            }
        }
    }
}
