using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

using Xunit;

namespace VAL.Tests.Architecture
{
    public sealed class ProjectDependencyTests
    {
        private static readonly string[] ProductProjects =
        {
            "VAL.Desktop",
            "VAL.App",
            "VAL.Abyss",
            "VAL.Continuum",
            "VAL.Contracts",
            "VAL.Host",
            "VAL.Truth",
        };

        private static readonly string[] DesktopDependencies = { "VAL.App" };

        [Fact]
        public void ProductProjectReferencesAreAcyclic()
        {
            var graph = ProductProjects.ToDictionary(
                project => project,
                ReadProjectReferences,
                StringComparer.OrdinalIgnoreCase);

            foreach (var project in ProductProjects)
            {
                AssertNoCycle(project, graph, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            }
        }

        [Fact]
        public void DesktopProjectReferencesOnlyApplicationComposition()
        {
            Assert.Equal(DesktopDependencies, ReadProjectReferences("VAL.Desktop"));
        }

        [Fact]
        public void TestsDoNotReferenceDesktopExecutable()
        {
            Assert.DoesNotContain(
                "VAL.Desktop",
                ReadProjectReferences("VAL.Tests"),
                StringComparer.OrdinalIgnoreCase);
        }

        private static string[] ReadProjectReferences(string projectName)
        {
            var mainDirectory = Path.Combine(FindRepositoryRoot(), "MAIN");
            var projectPath = Path.Combine(mainDirectory, projectName, $"{projectName}.csproj");
            var document = XDocument.Load(projectPath);

            return document
                .Descendants("ProjectReference")
                .Select(element => element.Attribute("Include")?.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => Path.GetFileNameWithoutExtension(value!))
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static void AssertNoCycle(
            string project,
            IReadOnlyDictionary<string, string[]> graph,
            HashSet<string> path)
        {
            Assert.True(path.Add(project), $"Project reference cycle detected at {project}.");

            if (graph.TryGetValue(project, out var dependencies))
            {
                foreach (var dependency in dependencies.Where(graph.ContainsKey))
                {
                    AssertNoCycle(dependency, graph, path);
                }
            }

            path.Remove(project);
        }

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "VAL.sln")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName ??
                throw new DirectoryNotFoundException("Could not locate the VAL repository root.");
        }
    }
}
