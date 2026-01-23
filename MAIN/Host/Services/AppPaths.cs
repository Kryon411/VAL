using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Options;
using VAL.Host.Options;

namespace VAL.Host.Services
{
    public sealed class AppPaths : IAppPaths
    {
        public string DataRoot { get; }
        public string LogsRoot { get; }
        public string ModulesRoot { get; }
        public string ProfileRoot { get; }

        public AppPaths(IOptions<ValOptions> options)
        {
            var warnings = new List<string>();
            var config = options.Value;

            var defaultDataRoot = ValOptions.DefaultDataRoot;
            var defaultModulesRoot = ResolveModulesRoot();

            DataRoot = NormalizePath(config.DataRoot, defaultDataRoot, "DataRoot", warnings, defaultDataRoot);
            LogsRoot = NormalizePath(config.LogsPath, Path.Combine(DataRoot, ValOptions.DefaultLogsPath), "LogsPath", warnings, DataRoot);
            ProfileRoot = NormalizePath(config.ProfilePath, Path.Combine(DataRoot, ValOptions.DefaultProfilePath), "ProfilePath", warnings, DataRoot);
            ModulesRoot = NormalizePath(config.ModulesPath, defaultModulesRoot, "ModulesPath", warnings, defaultModulesRoot);

            var logFile = Path.Combine(LogsRoot, "VAL.log");
            ValLog.Configure(logFile, config.EnableVerboseLogging);

            ValLog.Verbose(nameof(AppPaths), $"Resolved DataRoot: {DataRoot}");
            ValLog.Verbose(nameof(AppPaths), $"Resolved LogsRoot: {LogsRoot}");
            ValLog.Verbose(nameof(AppPaths), $"Resolved ProfileRoot: {ProfileRoot}");
            ValLog.Verbose(nameof(AppPaths), $"Resolved ModulesRoot: {ModulesRoot}");

            foreach (var warning in warnings)
            {
                ValLog.Warn(nameof(AppPaths), warning);
            }
        }

        private static string ResolveModulesRoot()
        {
            string exeDir = AppContext.BaseDirectory;
            string cwd = Directory.GetCurrentDirectory();

            static string FindWithModules(string startDir, int maxUp)
            {
                string dir = startDir;
                for (int i = 0; i <= maxUp; i++)
                {
                    if (Directory.Exists(Path.Combine(dir, "Modules")))
                        return dir;

                    var parent = Directory.GetParent(dir);
                    if (parent == null)
                        break;

                    dir = parent.FullName;
                }

                return startDir;
            }

            try
            {
                var fromExe = FindWithModules(exeDir, 6);
                if (Directory.Exists(Path.Combine(fromExe, "Modules")))
                    return fromExe;
            }
            catch
            {
                ValLog.Warn(nameof(AppPaths), "Failed to resolve modules root from executable directory.");
            }

            try
            {
                string dir = exeDir;
                for (int i = 0; i < 10; i++)
                {
                    var parent = Directory.GetParent(dir);
                    if (parent == null)
                        break;

                    dir = parent.FullName;
                    var candidate = Path.Combine(dir, "PRODUCT");
                    if (Directory.Exists(Path.Combine(candidate, "Modules")))
                        return candidate;
                }
            }
            catch
            {
                ValLog.Warn(nameof(AppPaths), "Failed to resolve modules root from sibling PRODUCT directory.");
            }

            try
            {
                var fromCwd = FindWithModules(cwd, 6);
                if (Directory.Exists(Path.Combine(fromCwd, "Modules")))
                    return fromCwd;
            }
            catch
            {
                ValLog.Warn(nameof(AppPaths), "Failed to resolve modules root from current directory.");
            }

            return exeDir;
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
