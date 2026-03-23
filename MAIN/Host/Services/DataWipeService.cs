using System;
using System.IO;

namespace VAL.Host.Services
{
    public sealed class DataWipeService : IDataWipeService
    {
        private readonly IAppPaths _appPaths;
        private readonly IPortalRuntimeStateManager _portalRuntimeStateManager;

        public DataWipeService(IAppPaths appPaths, IPortalRuntimeStateManager portalRuntimeStateManager)
        {
            _appPaths = appPaths ?? throw new ArgumentNullException(nameof(appPaths));
            _portalRuntimeStateManager = portalRuntimeStateManager ?? throw new ArgumentNullException(nameof(portalRuntimeStateManager));
        }

        public DataWipeResult WipeData()
        {
            int deleted = 0;
            int failed = 0;

            TryDeleteDirectory(_appPaths.LogsRoot, "Logs", ref deleted, ref failed);
            TryDeleteDirectory(_appPaths.ProfileRoot, "WebView profile", ref deleted, ref failed);
            TryDeleteDirectory(Path.Combine(_appPaths.DataRoot, "Snapshots"), "Snapshots", ref deleted, ref failed);
            TryDeleteDirectory(Path.Combine(_appPaths.DataRoot, "Staging"), "Staging", ref deleted, ref failed);

            TryDeleteDirectory(_appPaths.MemoryChatsRoot, "Continuum memory", ref deleted, ref failed);

            try
            {
                _portalRuntimeStateManager.ClearStaging();
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

    }
}
