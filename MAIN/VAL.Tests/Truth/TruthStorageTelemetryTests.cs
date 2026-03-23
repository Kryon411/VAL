using System;
using System.IO;
using VAL.Continuum.Pipeline.Truth;
using Xunit;

namespace VAL.Tests.Truth
{
    public sealed class TruthStoreTelemetryTests
    {
        [Fact]
        public void AppendTruthLinePublishesBytesThroughTruthTelemetryPublisher()
        {
            var chatId = Guid.NewGuid().ToString("N");
            var observedChatId = string.Empty;
            var observedBytes = 0L;
            var telemetryPublisher = new RecordingTruthTelemetryPublisher((id, bytes) =>
            {
                observedChatId = id;
                observedBytes = bytes;
            });
            var truthStore = new TruthStore(telemetryPublisher);

            CleanupChat(chatId);

            try
            {
                truthStore.AppendTruthLine(chatId, 'U', "hello");

                Assert.Equal(chatId, observedChatId);
                Assert.True(observedBytes > 0);
                Assert.True(File.Exists(TruthStorage.GetTruthPath(chatId)));
            }
            finally
            {
                CleanupChat(chatId);
            }
        }

        [Fact]
        public void AppendTruthLineWithoutThresholdMonitorStillWritesTruthLog()
        {
            var chatId = Guid.NewGuid().ToString("N");
            var truthStore = new TruthStore(NullTruthTelemetryPublisher.Instance);

            CleanupChat(chatId);

            try
            {
                truthStore.AppendTruthLine(chatId, 'A', "reply");

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

        private sealed class RecordingTruthTelemetryPublisher : ITruthTelemetryPublisher
        {
            private readonly Action<string, long> _publish;

            public RecordingTruthTelemetryPublisher(Action<string, long> publish)
            {
                _publish = publish;
            }

            public void PublishTruthBytes(string chatId, long bytes)
            {
                _publish(chatId, bytes);
            }
        }
    }
}
