using System;
using System.IO;
using Microsoft.Extensions.Options;
using VAL.Host;
using VAL.Host.Options;
using VAL.Host.Services;
using Xunit;

namespace VAL.Tests.Hosting
{
    public sealed class AppPathsTests
    {
        [Fact]
        public void AppPathsUsesNestedProductRootForModulesStateAndMemory()
        {
            var root = CreateTempRoot();
            var productRoot = Path.Combine(root, "PRODUCT");
            Directory.CreateDirectory(Path.Combine(productRoot, "Modules"));
            Directory.CreateDirectory(Path.Combine(productRoot, "Dock"));

            try
            {
                var paths = CreateAppPaths(root);

                Assert.Equal(Path.GetFullPath(root), paths.ContentRoot);
                Assert.Equal(Path.GetFullPath(productRoot), paths.ProductRoot);
                Assert.Equal(Path.Combine(productRoot, "Modules"), paths.ModulesRoot);
                Assert.Equal(Path.Combine(productRoot, "State"), paths.StateRoot);
                Assert.Equal(Path.Combine(productRoot, "Memory", "Chats"), paths.MemoryChatsRoot);
            }
            finally
            {
                TryDelete(root);
            }
        }

        [Fact]
        public void AppPathsKeepsContentRootWhenProductAssetsLiveThere()
        {
            var root = CreateTempRoot();
            Directory.CreateDirectory(Path.Combine(root, "Modules"));
            Directory.CreateDirectory(Path.Combine(root, "Dock"));

            try
            {
                var paths = CreateAppPaths(root);

                Assert.Equal(Path.GetFullPath(root), paths.ContentRoot);
                Assert.Equal(Path.GetFullPath(root), paths.ProductRoot);
                Assert.Equal(Path.Combine(root, "Modules"), paths.ModulesRoot);
                Assert.Equal(Path.Combine(root, "State"), paths.StateRoot);
                Assert.Equal(Path.Combine(root, "Memory", "Chats"), paths.MemoryChatsRoot);
            }
            finally
            {
                TryDelete(root);
            }
        }

        private static AppPaths CreateAppPaths(string contentRoot)
        {
            var dataRoot = Path.Combine(contentRoot, "data");
            var options = Options.Create(new ValOptions
            {
                DataRoot = dataRoot,
                LogsPath = "Logs",
                ProfilePath = "Profile",
                ModulesPath = string.Empty,
            });

            return new AppPaths(options, contentRoot);
        }

        private static string CreateTempRoot()
        {
            var path = Path.Combine(Path.GetTempPath(), "val-app-paths-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
            }
            catch
            {
                // Ignore cleanup failures in temp directories.
            }
        }

    }
}
