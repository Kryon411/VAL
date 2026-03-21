using System;
using System.IO;
using VAL.Continuum.Pipeline.Truth;
using Xunit;

namespace VAL.Tests.Truth
{
    public sealed class TruthStorageTelemetryTests
    {
        [Fact]
        public void AppendTruthLinePublishesBytesThroughTruthTelemetryBridge()
        {
            var chatId = Guid.NewGuid().ToString("N");
            var observedChatId = string.Empty;
            var observedBytes = 0L;

            CleanupChat(chatId);
            TruthTelemetryBridge.Configure((id, bytes) =>
            {
                observedChatId = id;
                observedBytes = bytes;
            });

            try
            {
                TruthStorage.AppendTruthLine(chatId, 'U', "hello");

                Assert.Equal(chatId, observedChatId);
                Assert.True(observedBytes > 0);
                Assert.True(File.Exists(TruthStorage.GetTruthPath(chatId)));
            }
            finally
            {
                TruthTelemetryBridge.Configure(null);
                CleanupChat(chatId);
            }
        }

        [Fact]
        public void AppendTruthLineWithoutConfiguredBridgeStillWritesTruthLog()
        {
            var chatId = Guid.NewGuid().ToString("N");

            CleanupChat(chatId);
            TruthTelemetryBridge.Configure(null);

            try
            {
                TruthStorage.AppendTruthLine(chatId, 'A', "reply");

                var truthPath = TruthStorage.GetTruthPath(chatId);
                Assert.True(File.Exists(truthPath));
                Assert.Contains("A|reply", File.ReadAllText(truthPath));
            }
            finally
            {
                CleanupChat(chatId);
            }
        }

        private static void CleanupChat(string chatId)
        {
            try
            {
                var dir = TruthStorage.GetChatDir(chatId);
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }
}
