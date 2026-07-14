using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using Xunit;

namespace VAL.Tests.Modules
{
    public sealed class ModuleManifestContractTests
    {
        [Fact]
        public void ModuleJsonFilesParseAndContainRequiredFields()
        {
            var repoRoot = ResolveRepositoryRoot();
            var manifests = Directory.EnumerateFiles(Path.Combine(repoRoot, "MAIN"), "*.module.json", SearchOption.AllDirectories)
                .Where(path => !ContainsBuildOutputSegment(path))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            Assert.NotEmpty(manifests);
            var moduleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var manifestPath in manifests)
            {
                using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
                var error = ValidateManifest(document.RootElement);
                Assert.True(string.IsNullOrEmpty(error), $"{Path.GetRelativePath(repoRoot, manifestPath)} validation failed: {error}");

                var moduleName = document.RootElement.GetProperty("name").GetString();
                Assert.True(moduleNames.Add(moduleName!), $"Duplicate module name: {moduleName}");
                AssertReferencedAssetsExist(document.RootElement, manifestPath, "entryScripts");
                AssertReferencedAssetsExist(document.RootElement, manifestPath, "styles");
            }
        }

        [Fact]
        public void ValidateManifestReturnsClearErrorWhenRequiredFieldMissing()
        {
            using var document = JsonDocument.Parse("""
            {
              "name": "Broken",
              "apiVersion": "1",
              "hostMinVersion": "0.0.0",
              "capabilities": ["ui"],
              "enabled": true,
              "entryScripts": ["Broken.js"]
            }
            """);

            var error = ValidateManifest(document.RootElement);

            Assert.Equal("version is required.", error);
        }

        [Fact]
        public void ValidateManifestReturnsClearErrorWhenApiVersionInvalid()
        {
            using var document = JsonDocument.Parse("""
            {
              "name": "Broken",
              "version": "1.0.0",
              "apiVersion": "2",
              "hostMinVersion": "0.0.0",
              "capabilities": ["ui"],
              "enabled": true,
              "entryScripts": ["Broken.js"]
            }
            """);

            var error = ValidateManifest(document.RootElement);

            Assert.Equal("apiVersion must be '1'.", error);
        }

        private static string? ValidateManifest(JsonElement root)
        {
            if (!TryReadNonEmptyString(root, "name", out _))
                return "name is required.";

            if (!TryReadNonEmptyString(root, "version", out var version))
                return "version is required.";

            if (!Version.TryParse(version, out _))
                return "version must be a valid semantic version.";

            if (!TryReadNonEmptyString(root, "apiVersion", out var apiVersion))
                return "apiVersion is required.";

            if (!string.Equals(apiVersion, "1", StringComparison.OrdinalIgnoreCase))
                return "apiVersion must be '1'.";

            if (!TryReadNonEmptyString(root, "hostMinVersion", out _)
                && !TryReadNonEmptyString(root, "minHostVersion", out _))
                return "hostMinVersion is required.";

            if (!root.TryGetProperty("capabilities", out var capabilities)
                || capabilities.ValueKind != JsonValueKind.Array
                || capabilities.GetArrayLength() == 0)
                return "capabilities is required.";

            foreach (var capability in capabilities.EnumerateArray())
            {
                if (capability.ValueKind != JsonValueKind.String
                    || !string.Equals(capability.GetString(), "ui", StringComparison.OrdinalIgnoreCase))
                    return "capabilities must only include 'ui'.";
            }

            if (!root.TryGetProperty("enabled", out var enabled)
                || (enabled.ValueKind != JsonValueKind.True && enabled.ValueKind != JsonValueKind.False))
                return "enabled must be specified.";

            if (!root.TryGetProperty("entryScripts", out var entryScripts)
                || entryScripts.ValueKind != JsonValueKind.Array
                || entryScripts.GetArrayLength() == 0)
                return "entryScripts must include at least one script.";

            foreach (var entryScript in entryScripts.EnumerateArray())
            {
                if (entryScript.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(entryScript.GetString()))
                    return "entryScripts contains an empty path.";
            }

            return null;
        }

        private static void AssertReferencedAssetsExist(JsonElement root, string manifestPath, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var assets) || assets.ValueKind != JsonValueKind.Array)
                return;

            var moduleDirectory = Path.GetDirectoryName(manifestPath)!;
            var expectedPrefix = Path.GetFullPath(moduleDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            foreach (var asset in assets.EnumerateArray())
            {
                var relativePath = asset.GetString();
                Assert.False(Path.IsPathRooted(relativePath), $"{propertyName} must use relative paths: {relativePath}");

                var assetPath = Path.GetFullPath(Path.Combine(moduleDirectory, relativePath!));
                Assert.StartsWith(expectedPrefix, assetPath, StringComparison.OrdinalIgnoreCase);
                Assert.True(File.Exists(assetPath), $"Referenced module asset does not exist: {assetPath}");
            }
        }

        private static bool TryReadNonEmptyString(JsonElement root, string propertyName, out string? value)
        {
            value = null;
            if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
                return false;

            value = property.GetString();
            return !string.IsNullOrWhiteSpace(value);
        }

        private static bool ContainsBuildOutputSegment(string path)
        {
            var segments = Path.GetRelativePath(ResolveRepositoryRoot(), path)
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return segments.Contains("bin", StringComparer.OrdinalIgnoreCase) ||
                   segments.Contains("obj", StringComparer.OrdinalIgnoreCase);
        }

        private static string ResolveRepositoryRoot()
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName, "VAL.sln")))
                    return current.FullName;

                current = current.Parent;
            }

            throw new InvalidOperationException("Could not locate repository root from test base directory.");
        }
    }
}
