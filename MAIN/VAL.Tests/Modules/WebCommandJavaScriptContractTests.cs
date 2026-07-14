using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using VAL.Contracts;

using Xunit;

namespace VAL.Tests.Modules
{
    public sealed partial class WebCommandJavaScriptContractTests
    {
        [Fact]
        public void JavaScriptHostCommandsExistInCanonicalContract()
        {
            var repositoryRoot = FindRepositoryRoot();
            var roots = new[]
            {
                Path.Combine(repositoryRoot, "MAIN", "Modules"),
                Path.Combine(repositoryRoot, "MAIN", "Dock"),
            };
            var contractValues = WebCommandNames.GetAll().Values.ToHashSet(StringComparer.Ordinal);
            var unknownCommands = new SortedSet<string>(StringComparer.Ordinal);

            foreach (var scriptPath in roots.SelectMany(root =>
                         Directory.EnumerateFiles(root, "*.js", SearchOption.AllDirectories)))
            {
                var script = File.ReadAllText(scriptPath);
                foreach (Match match in ContractValueRegex().Matches(script))
                {
                    var value = match.Groups["value"].Value;
                    if (LooksLikeHostCommand(value) && !contractValues.Contains(value))
                        unknownCommands.Add(value);
                }
            }

            Assert.True(
                unknownCommands.Count == 0,
                $"JavaScript uses commands absent from WebCommandNames: {string.Join(", ", unknownCommands)}");
        }

        private static bool LooksLikeHostCommand(string value)
        {
            return value.Contains(".command.", StringComparison.Ordinal) ||
                   value.StartsWith("continuum.", StringComparison.Ordinal) ||
                   value.Equals("dock.ui_state.get", StringComparison.Ordinal) ||
                   value.Equals("dock.ui_state.set", StringComparison.Ordinal) ||
                   value.StartsWith("tools.open_", StringComparison.Ordinal);
        }

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "VAL.sln")))
                directory = directory.Parent;

            return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");
        }

        [GeneratedRegex("[\\\"'](?<value>(?:abyss|continuum|portal|privacy|tools|nav|dock|void|truth)\\.[A-Za-z0-9_.]+)[\\\"']")]
        private static partial Regex ContractValueRegex();
    }
}
