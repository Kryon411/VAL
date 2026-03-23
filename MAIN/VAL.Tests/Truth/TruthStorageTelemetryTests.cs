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
            var root = CreateTempRoot();
            var chatId = Guid.NewGuid().ToString("N");
            var observedChatId = string.Empty;
            var observedBytes = 0L;
            var telemetryPublisher = new RecordingTruthTelemetryPublisher((id, bytes) =>
            {
                observedChatId = id;
                observedBytes = bytes;
            });
            var truthStore = new TruthStore(telemetryPublisher, root);

            try
            {
                truthStore.AppendTruthLine(chatId, 'U', "hello");

                Assert.Equal(chatId, observedChatId);
                Assert.True(observedBytes > 0);
                Assert.True(File.Exists(truthStore.GetTruthPath(chatId)));
            }
            finally
            {
                CleanupRoot(root);
            }
        }

        [Fact]
        public void AppendTruthLineWithoutThresholdMonitorStillWritesTruthLog()
        {
            var root = CreateTempRoot();
            var chatId = Guid.NewGuid().ToString("N");
            var truthStore = new TruthStore(NullTruthTelemetryPublisher.Instance, root);

            try
            {
                truthStore.AppendTruthLine(chatId, 'A', "reply");

                var truthPath = truthStore.GetTruthPath(chatId);
                Assert.True(File.Exists(truthPath));
                Assert.Contains("A|reply", File.ReadAllText(truthPath));
            }
            finally
            {
                CleanupRoot(root);
            }
        }

        [Fact]
        public void GetChatDirUsesConfiguredMemoryChatsRoot()
        {
            var root = CreateTempRoot();
            var chatId = Guid.NewGuid().ToString("N");
            var truthStore = new TruthStore(NullTruthTelemetryPublisher.Instance, root);

            try
            {
                Assert.Equal(Path.Combine(root, chatId), truthStore.GetChatDir(chatId));
            }
            finally
            {
                CleanupRoot(root);
            }
        }

        private static string CreateTempRoot()
        {
            var root = Path.Combine(Path.GetTempPath(), "val-truth-store-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        private static void CleanupRoot(string root)
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
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
