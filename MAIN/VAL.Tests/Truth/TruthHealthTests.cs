using System;
using System.IO;
using VAL.Continuum.Pipeline.Truth;
using VAL.Host;
using VAL.Host.Services;
using Xunit;

namespace VAL.Tests.Truth
{
    public sealed class TruthHealthTests
    {
        [Fact]
        public void Build_ReturnsExpectedCounts()
        {
            var (dir, path, cleanup) = CreateTempTruthLog();
            try
            {
                File.WriteAllText(path, string.Join(string.Empty, new[]
                {
                    "A|one\r\n",
                    "BAD LINE\r\n",
                    "U|two\r\n"
                }));

                var report = TruthHealth.Build("chat-1", path, Path.Combine(dir, "Truth.repair.log"), repairTailFirst: false);

                Assert.Equal("chat-1", report.ChatId);
                Assert.Equal(new FileInfo(path).Length, report.Bytes);
                Assert.Equal(3, report.PhysicalLineCount);
                Assert.Equal(2, report.ParsedEntryCount);
                Assert.Equal(3, report.LastParsedPhysicalLineNumber);
            }
            finally
            {
                cleanup();
            }
        }

        [Fact]
        public void TruthHealthService_UsesActiveChatId()
        {
            var chatId = $"chat-{Guid.NewGuid():N}";
            var chatDir = TruthStorage.GetChatDir(chatId);
            var truthPath = TruthStorage.GetTruthPath(chatId);

            try
            {
                Directory.CreateDirectory(chatDir);
                File.WriteAllText(truthPath, "A|hello\r\n");
                SessionContext.SetActiveChatId(chatId);

                var service = new TruthHealthService();
                var snapshot = service.GetSnapshot();

                Assert.True(snapshot.HasChat);
                Assert.Equal(chatId, snapshot.ChatId);
                Assert.NotNull(snapshot.Report);
                Assert.Equal(1, snapshot.Report!.ParsedEntryCount);
            }
            finally
            {
                TryDeleteDir(chatDir);
            }
        }

        private static (string dir, string path, Action cleanup) CreateTempTruthLog()
        {
            var dir = Path.Combine(Path.GetTempPath(), "VAL.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "Truth.log");

            return (dir, path, () => TryDeleteDir(dir));
        }

        private static void TryDeleteDir(string path)
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
    }
}
