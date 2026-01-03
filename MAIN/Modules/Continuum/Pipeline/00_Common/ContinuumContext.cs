using System;
using System.IO;

namespace VAL.Continuum.Pipeline.Common
{
    /// <summary>
    /// Loads the Continuum "Context.txt" preamble and resolves the PRODUCT/MAIN roots robustly.
    /// </summary>
    internal static class ContinuumContext
    {
        public const string ContextFileName = "Context.txt";

        /// <summary>
        /// Returns the best-effort absolute path to Context.txt (prefers PRODUCT over MAIN when both exist).
        /// Returns empty string if not found.
        /// </summary>
        public static string ResolveContextPath()
        {
            // 1) Prefer repo-root PRODUCT/Modules/Continuum/Context.txt if we can detect the repo root.
            var repoRoot = TryFindRepoRoot();
            if (!string.IsNullOrWhiteSpace(repoRoot))
            {
                var productPath = Path.Combine(repoRoot, "PRODUCT", "Modules", "Continuum", ContextFileName);
                if (File.Exists(productPath)) return productPath;

                var mainPath = Path.Combine(repoRoot, "MAIN", "Modules", "Continuum", ContextFileName);
                if (File.Exists(mainPath)) return mainPath;
            }

            // 2) Next, prefer PRODUCT root if the app is running from within PRODUCT.
            var productRoot = ResolveProductRoot();
            if (!string.IsNullOrWhiteSpace(productRoot))
            {
                var direct = Path.Combine(productRoot, "Modules", "Continuum", ContextFileName);
                if (File.Exists(direct)) return direct;

                // In some layouts ResolveProductRoot may return the repository root; also probe PRODUCT/ beneath it.
                var nested = Path.Combine(productRoot, "PRODUCT", "Modules", "Continuum", ContextFileName);
                if (File.Exists(nested)) return nested;
            }

            // 3) Fallback: current working directory probe.
            try
            {
                var cwd = Directory.GetCurrentDirectory();
                var cwdProduct = Path.Combine(cwd, "PRODUCT", "Modules", "Continuum", ContextFileName);
                if (File.Exists(cwdProduct)) return cwdProduct;

                var cwdMain = Path.Combine(cwd, "MAIN", "Modules", "Continuum", ContextFileName);
                if (File.Exists(cwdMain)) return cwdMain;
            }
            catch { /* ignore */ }

            return string.Empty;
        }

        /// <summary>
        /// Loads Context.txt (preamble) if present, else returns empty string.
        /// </summary>
        public static string LoadContextText()
        {
            try
            {
                var path = ResolveContextPath();
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return string.Empty;

                var raw = File.ReadAllText(path);
                return NormalizeNewlines(raw).Trim();
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Attempts to resolve the PRODUCT root (directory that contains Modules/Continuum).
        /// Returns AppContext.BaseDirectory as a last resort.
        /// </summary>
        public static string ResolveProductRoot()
        {
            // Candidate starting points: base directory, current directory, and process directory.
            var candidates = new[]
            {
                SafeDir(AppContext.BaseDirectory),
                SafeDir(Environment.ProcessPath),
                SafeDir(Directory.GetCurrentDirectory()),
            };

            foreach (var start in candidates)
            {
                var found = TryFindProductRootFrom(start);
                if (!string.IsNullOrWhiteSpace(found)) return found;
            }

            // Last resort: base directory.
            return SafeDir(AppContext.BaseDirectory);
        }

        private static string TryFindRepoRoot()
        {
            var candidates = new[]
            {
                SafeDir(AppContext.BaseDirectory),
                SafeDir(Environment.ProcessPath),
                SafeDir(Directory.GetCurrentDirectory()),
            };

            foreach (var start in candidates)
            {
                var dir = start;
                for (var depth = 0; depth < 10 && !string.IsNullOrWhiteSpace(dir); depth++)
                {
                    var main = Path.Combine(dir, "MAIN");
                    var product = Path.Combine(dir, "PRODUCT");
                    if (Directory.Exists(main) && Directory.Exists(product)) return dir;

                    var parent = Directory.GetParent(dir);
                    dir = parent?.FullName ?? string.Empty;
                }
            }

            return string.Empty;
        }

        private static string TryFindProductRootFrom(string startDir)
        {
            if (string.IsNullOrWhiteSpace(startDir)) return string.Empty;

            var dir = startDir;

            for (var depth = 0; depth < 10 && !string.IsNullOrWhiteSpace(dir); depth++)
            {
                // Case A: dir IS the product root.
                if (Directory.Exists(Path.Combine(dir, "Modules", "Continuum"))) return dir;

                // Case B: dir contains PRODUCT/Modules/Continuum.
                var productDir = Path.Combine(dir, "PRODUCT");
                if (Directory.Exists(Path.Combine(productDir, "Modules", "Continuum"))) return productDir;

                var parent = Directory.GetParent(dir);
                dir = parent?.FullName ?? string.Empty;
            }

            return string.Empty;
        }

        private static string SafeDir(string? pathOrFile)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(pathOrFile)) return string.Empty;

                // Environment.ProcessPath returns a file; AppContext.BaseDirectory returns a directory.
                if (File.Exists(pathOrFile)) return Path.GetDirectoryName(pathOrFile) ?? string.Empty;
                if (Directory.Exists(pathOrFile)) return pathOrFile;

                // If neither exists, still attempt to treat it as a directory.
                return Path.GetDirectoryName(pathOrFile) ?? pathOrFile;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string NormalizeNewlines(string s)
        {
            return s.Replace("\r\n", "\n").Replace("\r", "\n");
        }
    }
}