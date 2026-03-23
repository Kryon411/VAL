using System;
using System.Collections.Generic;
using System.IO;

namespace VAL.Host.Services
{
    public static class AppPathLayout
    {
        public static string ResolveContentRoot(ICollection<string>? warnings = null)
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
                warnings?.Add("Failed to resolve content root from executable path. Falling back to AppContext.BaseDirectory.");
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

        public static string ResolveProductRoot(string? contentRoot = null)
        {
            var root = NormalizeRoot(contentRoot, ResolveContentRoot());
            if (ContainsProductContent(root))
                return root;

            var productDir = Path.Combine(root, "PRODUCT");
            if (ContainsProductContent(productDir))
                return Path.GetFullPath(productDir);

            var mainDir = Path.Combine(root, "MAIN");
            if (Directory.Exists(Path.Combine(mainDir, "Modules")))
            {
                var devProduct = Path.Combine(mainDir, "PRODUCT");
                return Directory.Exists(devProduct)
                    ? Path.GetFullPath(devProduct)
                    : root;
            }

            return root;
        }

        public static string ResolveMemoryChatsRoot(string? productRoot = null)
        {
            var root = NormalizeRoot(productRoot, ResolveProductRoot());
            return Path.Combine(root, "Memory", "Chats");
        }

        private static bool ContainsProductContent(string root)
        {
            return Directory.Exists(Path.Combine(root, "Modules")) ||
                   Directory.Exists(Path.Combine(root, "Dock"));
        }

        private static string NormalizeRoot(string? value, string fallback)
        {
            var candidate = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
            try
            {
                return Path.GetFullPath(candidate);
            }
            catch
            {
                return fallback;
            }
        }
    }
}
