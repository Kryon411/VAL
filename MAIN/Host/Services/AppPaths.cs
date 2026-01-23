using System;
using System.IO;

namespace VAL.Host.Services
{
    public sealed class AppPaths : IAppPaths
    {
        public string DataRoot { get; }
        public string LogsRoot { get; }
        public string ModulesRoot { get; }
        public string ProfileRoot { get; }

        public AppPaths()
        {
            DataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VAL");
            LogsRoot = Path.Combine(DataRoot, "Logs");
            ProfileRoot = Path.Combine(DataRoot, "Profile");
            ModulesRoot = ResolveModulesRoot();
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
    }
}
