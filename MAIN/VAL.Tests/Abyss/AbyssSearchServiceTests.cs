using System;
using System.IO;
using VAL.Host;
using VAL.Host.Abyss;
using Xunit;

namespace VAL.Tests.Abyss
{
    public sealed class AbyssSearchServiceTests
    {
        private static readonly string[] InitialTruthLines =
        {
            "U|alpha question",
            "A|first answer"
        };

        private static readonly string[] UpdatedTruthLines =
        {
            "U|beta question",
            "A|updated answer"
        };

        private static readonly string[] OlderTruthLines =
        {
            "U|older prompt",
            "A|older answer"
        };

        private static readonly string[] NewerTruthLines =
        {
            "U|first prompt",
            "A|first answer",
            "U|second prompt",
            "A|second answer",
            "U|third prompt",
            "A|third answer"
        };

        [Fact]
        public void SearchRefreshesCachedTruthLogWhenFileChanges()
        {
            var root = CreateTempRoot();
            var chatId = Guid.NewGuid().ToString();
            var truthPath = CreateTruthPath(root, chatId);
            var service = new AbyssSearchService(new FakeLog());

            try
            {
                WriteTruthLog(
                    truthPath,
                    InitialTruthLines,
                    new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

                var firstResults = service.Search(root, "alpha", 4);

                Assert.Single(firstResults);
                Assert.Contains("alpha question", firstResults[0].Exchange.UserText);

                WriteTruthLog(
                    truthPath,
                    UpdatedTruthLines,
                    new DateTime(2026, 1, 1, 0, 5, 0, DateTimeKind.Utc));

                var secondResults = service.Search(root, "beta", 4);

                Assert.Single(secondResults);
                Assert.Contains("beta question", secondResults[0].Exchange.UserText);
                Assert.DoesNotContain("alpha question", secondResults[0].Exchange.UserText);
            }
            finally
            {
                TryDelete(root);
            }
        }

        [Fact]
        public void GetLastFromMostRecentReturnsNewestExchangesFirst()
        {
            var root = CreateTempRoot();
            var olderChatId = Guid.NewGuid().ToString();
            var newerChatId = Guid.NewGuid().ToString();
            var service = new AbyssSearchService(new FakeLog());

            try
            {
                WriteTruthLog(
                    CreateTruthPath(root, olderChatId),
                    OlderTruthLines,
                    new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

                WriteTruthLog(
                    CreateTruthPath(root, newerChatId),
                    NewerTruthLines,
                    new DateTime(2026, 1, 1, 1, 0, 0, DateTimeKind.Utc));

                var results = service.GetLastFromMostRecent(root, 2);

                Assert.Equal(2, results.Count);
                Assert.Equal(newerChatId, results[0].ChatId);
                Assert.Equal("third prompt", results[0].UserText);
                Assert.Equal("second prompt", results[1].UserText);
            }
            finally
            {
                TryDelete(root);
            }
        }

        private static string CreateTempRoot()
        {
            var root = Path.Combine(Path.GetTempPath(), "val-abyss-search-service", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        private static string CreateTruthPath(string root, string chatId)
        {
            var dir = Path.Combine(root, chatId);
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "Truth.log");
        }

        private static void WriteTruthLog(string truthPath, string[] lines, DateTime lastWriteUtc)
        {
            File.WriteAllText(truthPath, string.Join(Environment.NewLine, lines));
            File.SetLastWriteTimeUtc(truthPath, lastWriteUtc);
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
                // best-effort cleanup
            }
        }

        private sealed class FakeLog : ILog
        {
            public void Info(string category, string message) { }
            public void Warn(string category, string message) { }
            public void LogError(string category, string message) { }
            public void Verbose(string category, string message) { }
        }
    }
}
