using System;
using System.IO;
using VAL.Host.Portal;

namespace VAL.Host.Services
{
    public sealed class DataWipeService : IDataWipeService
    {
        private readonly IAppPaths _appPaths;

        public DataWipeService(IAppPaths appPaths)
        {
            _appPaths = appPaths ?? throw new ArgumentNullException(nameof(appPaths));
        }

        public DataWipeResult WipeData()
        {
            int deleted = 0;
            int failed = 0;

            TryDeleteDirectory(_appPaths.LogsRoot, "Logs", ref deleted, ref failed);
            TryDeleteDirectory(_appPaths.ProfileRoot, "WebView profile", ref deleted, ref failed);
            TryDeleteDirectory(Path.Combine(_appPaths.DataRoot, "Snapshots"), "Snapshots", ref deleted, ref failed);
            TryDeleteDirectory(Path.Combine(_appPaths.DataRoot, "Staging"), "Staging", ref deleted, ref failed);

            var memoryRoot = ResolveMemoryRoot();
            if (!string.IsNullOrWhiteSpace(memoryRoot))
                TryDeleteDirectory(memoryRoot, "Continuum memory", ref deleted, ref failed);

            try
            {
                PortalRuntime.ClearStaging();
            }
            catch
            {
                // Best-effort only.
            }

            EnsureDirectory(_appPaths.DataRoot);
            EnsureDirectory(_appPaths.LogsRoot);
            EnsureDirectory(_appPaths.ProfileRoot);

            var success = failed == 0;
            var partial = !success && deleted > 0;
            return new DataWipeResult(success, partial, deleted, failed);
        }

        private static void TryDeleteDirectory(string path, string label, ref int deleted, ref int failed)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            if (!Directory.Exists(path))
                return;

            try
            {
                Directory.Delete(path, recursive: true);
                deleted++;
                ValLog.Info(nameof(DataWipeService), $"Wiped {label} at {path}");
            }
            catch (Exception ex)
            {
                failed++;
                ValLog.Warn(nameof(DataWipeService), $"Failed to wipe {label} at {path}. {ex.GetType().Name}: {ex.Message}");
            }
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
                // Best-effort.
            }
        }

        private static string ResolveMemoryRoot()
        {
            try
            {
                var root = ResolveProductRoot();
                return Path.Combine(root, "Memory", "Chats");
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string ResolveProductRoot()
        {
            string bundleDir;
            try
            {
                var p = Environment.ProcessPath;
                bundleDir = !string.IsNullOrWhiteSpace(p)
                    ? (Path.GetDirectoryName(p) ?? AppContext.BaseDirectory)
                    : AppContext.BaseDirectory;
            }
            catch
            {
                bundleDir = AppContext.BaseDirectory;
            }

            if (Directory.Exists(Path.Combine(bundleDir, "Modules")) ||
                Directory.Exists(Path.Combine(bundleDir, "Dock")))
                return bundleDir;

            var productDir = Path.Combine(bundleDir, "PRODUCT");
            if (Directory.Exists(Path.Combine(productDir, "Modules")) ||
                Directory.Exists(Path.Combine(productDir, "Dock")))
                return productDir;

            var mainDir = Path.Combine(bundleDir, "MAIN");
            if (Directory.Exists(Path.Combine(mainDir, "Modules")))
            {
                var devProduct = Path.Combine(mainDir, "PRODUCT");
                return Directory.Exists(devProduct) ? devProduct : bundleDir;
            }

            return bundleDir;
        }
    }
}
