using System;
using System.Globalization;
using System.IO;
using VAL.Continuum.Pipeline.Truth;
using VAL.Host;
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
                var truthPath = Path.Combine(chatDir, TruthStorage.TruthFileName);
                File.WriteAllText(truthPath, "U|hello" + Environment.NewLine + "A|reply");

                var repairPath = Path.Combine(chatDir, "Truth.repair.log");
                var stamp = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                File.WriteAllText(repairPath, $"{stamp} truncated tail repair removed 42 bytes");

                SessionContext.SetActiveChatId(chatId);

                var service = new TruthHealthReportService(root);
                var result = service.GetCurrentSnapshot();

                Assert.True(result.HasActiveChat);
                Assert.Equal(chatId, result.ChatId);
                Assert.NotNull(result.Snapshot);
                Assert.Equal(chatId, result.Snapshot!.Report.ChatId);
                Assert.Equal(2, result.Snapshot.Report.ParsedEntryCount);
                Assert.Equal(2, result.Snapshot.Report.PhysicalLineCount);
                Assert.Equal(2, result.Snapshot.Report.LastParsedPhysicalLineNumber);
                Assert.Equal(42, result.Snapshot.Report.LastRepairBytesRemoved);
                Assert.Equal(Path.Combine("Memory", "Chats", chatId, TruthStorage.TruthFileName), result.Snapshot.RelativeTruthPath);
            }
            finally
            {
                try { Directory.Delete(root, recursive: true); } catch { }
            }
        }
    }
}
