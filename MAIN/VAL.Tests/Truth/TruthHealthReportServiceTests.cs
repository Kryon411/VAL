using System;
using System.Globalization;
using System.IO;
using Microsoft.Extensions.Options;
using VAL.Continuum.Pipeline.Truth;
using VAL.Host;
using VAL.Host.Options;
using VAL.Host.Services;
using Xunit;

namespace VAL.Tests.Truth
{
    public sealed class TruthHealthReportServiceTests
    {
        [Fact]
        public void GetCurrentSnapshotUsesSessionChatId()
        {
            var root = Path.Combine(Path.GetTempPath(), "val-truth-health-service", Guid.NewGuid().ToString("N"));
            var chatId = Guid.NewGuid().ToString();
            var chatDir = Path.Combine(root, "Memory", "Chats", chatId);
            Directory.CreateDirectory(chatDir);

            try
            {
                var truthPath = Path.Combine(chatDir, TruthStore.DefaultTruthFileName);
                File.WriteAllText(truthPath, "U|hello" + Environment.NewLine + "A|reply");

                var repairPath = Path.Combine(chatDir, "Truth.repair.log");
                var stamp = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                File.WriteAllText(repairPath, $"{stamp} truncated tail repair removed 42 bytes");

                var appPaths = CreateAppPaths(root);
                var sessionContext = new SessionContext();
                sessionContext.SetActiveChatId(chatId);
                var truthStore = new TruthStore(NullTruthTelemetryPublisher.Instance);

                var service = new TruthHealthReportService(appPaths, sessionContext, truthStore, root);
                var result = service.GetCurrentSnapshot();

                Assert.True(result.HasActiveChat);
                Assert.Equal(chatId, result.ChatId);
                Assert.NotNull(result.Snapshot);
                Assert.Equal(chatId, result.Snapshot!.Report.ChatId);
                Assert.Equal(2, result.Snapshot.Report.ParsedEntryCount);
                Assert.Equal(2, result.Snapshot.Report.PhysicalLineCount);
                Assert.Equal(2, result.Snapshot.Report.LastParsedPhysicalLineNumber);
                Assert.Equal(42, result.Snapshot.Report.LastRepairBytesRemoved);
                Assert.Equal(Path.Combine("Memory", "Chats", chatId, TruthStore.DefaultTruthFileName), result.Snapshot.RelativeTruthPath);
            }
            finally
            {
                try { Directory.Delete(root, recursive: true); } catch { }
            }
        }

        [Fact]
        public void GetCurrentSnapshotBuildsReportsWithoutActiveChatFromAppPaths()
        {
            var root = Path.Combine(Path.GetTempPath(), "val-truth-health-service", Guid.NewGuid().ToString("N"));
            var chatId = Guid.NewGuid().ToString();
            var chatDir = Path.Combine(root, "Memory", "Chats", chatId);
            Directory.CreateDirectory(chatDir);

            try
            {
                File.WriteAllText(Path.Combine(chatDir, "Truth.log"), "U|hello" + Environment.NewLine + "A|reply");

                var appPaths = CreateAppPaths(root);
                var sessionContext = new SessionContext();
                var truthStore = new TruthStore(NullTruthTelemetryPublisher.Instance);

                var service = new TruthHealthReportService(appPaths, sessionContext, truthStore);
                var result = service.GetCurrentSnapshot();

                Assert.False(result.HasActiveChat);
                Assert.Single(result.Reports);
                Assert.Equal(chatId, result.Reports[0].Report.ChatId);
            }
            finally
            {
                try { Directory.Delete(root, recursive: true); } catch { }
            }
        }

        private static AppPaths CreateAppPaths(string root)
        {
            var options = Options.Create(new ValOptions
            {
                DataRoot = Path.Combine(root, "Data"),
                LogsPath = "Logs",
                ProfilePath = "Profile",
            });

            return new AppPaths(options, root);
        }
    }
}
