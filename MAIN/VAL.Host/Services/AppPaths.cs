using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Options;
using VAL.Host.Options;

namespace VAL.Host.Services
{
    public sealed class AppPaths : IAppPaths
    {
        private readonly ILog _log;

        public string ContentRoot { get; }
        public string ProductRoot { get; }
        public string StateRoot { get; }
        public string DataRoot { get; }
        public string LogsRoot { get; }
        public string ModulesRoot { get; }
        public string MemoryChatsRoot { get; }
        public string ProfileRoot { get; }

        public AppPaths(IOptions<ValOptions> options, ILog log)
            : this(options, log, contentRootOverride: null)
        {
        }

        internal AppPaths(IOptions<ValOptions> options, ILog log, string? contentRootOverride)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
            var warnings = new List<string>();
            var config = options.Value;

            ContentRoot = string.IsNullOrWhiteSpace(contentRootOverride)
                ? AppPathLayout.ResolveContentRoot(warnings)
                : NormalizePath(contentRootOverride, AppContext.BaseDirectory, "ContentRoot", warnings, null);
            ProductRoot = AppPathLayout.ResolveProductRoot(ContentRoot);
            var defaultDataRoot = ValOptions.DefaultDataRoot;
            var defaultModulesRoot = Path.Combine(ProductRoot, "Modules");
            StateRoot = Path.Combine(ProductRoot, "State");
            MemoryChatsRoot = AppPathLayout.ResolveMemoryChatsRoot(ProductRoot);

            DataRoot = NormalizePath(config.DataRoot, defaultDataRoot, "DataRoot", warnings, defaultDataRoot);
            LogsRoot = NormalizePath(config.LogsPath, Path.Combine(DataRoot, ValOptions.DefaultLogsPath), "LogsPath", warnings, DataRoot);
            ProfileRoot = NormalizePath(config.ProfilePath, Path.Combine(DataRoot, ValOptions.DefaultProfilePath), "ProfilePath", warnings, DataRoot);
            ModulesRoot = NormalizePath(config.ModulesPath, defaultModulesRoot, "ModulesPath", warnings, ProductRoot);

            var logFile = Path.Combine(LogsRoot, "VAL.log");
            ValLog.Configure(logFile, config.EnableVerboseLogging);

            _log.Verbose(nameof(AppPaths), $"Resolved ContentRoot: {ContentRoot}");
            _log.Verbose(nameof(AppPaths), $"Resolved ProductRoot: {ProductRoot}");
            _log.Verbose(nameof(AppPaths), $"Resolved StateRoot: {StateRoot}");
            _log.Verbose(nameof(AppPaths), $"Resolved DataRoot: {DataRoot}");
            _log.Verbose(nameof(AppPaths), $"Resolved LogsRoot: {LogsRoot}");
            _log.Verbose(nameof(AppPaths), $"Resolved MemoryChatsRoot: {MemoryChatsRoot}");
            _log.Verbose(nameof(AppPaths), $"Resolved ProfileRoot: {ProfileRoot}");
            _log.Verbose(nameof(AppPaths), $"Resolved ModulesRoot: {ModulesRoot}");

            foreach (var warning in warnings)
            {
                _log.Warn(nameof(AppPaths), warning);
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
