using System;
using System.IO;

namespace VAL.Host.Services
{
    internal static class ProductRootResolver
    {
        public static string ResolveProductRoot()
        {
            string bundleDir;
            try
            {
                var processPath = Environment.ProcessPath;
                bundleDir = !string.IsNullOrWhiteSpace(processPath)
                    ? (Path.GetDirectoryName(processPath) ?? AppContext.BaseDirectory)
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
