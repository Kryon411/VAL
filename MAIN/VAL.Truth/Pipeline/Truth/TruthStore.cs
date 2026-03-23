using System;
using System.IO;
using System.Threading;

namespace VAL.Continuum.Pipeline.Truth
{
    public sealed class TruthStore : ITruthStore
    {
        private readonly ITruthTelemetryPublisher _telemetryPublisher;

        public TruthStore(ITruthTelemetryPublisher telemetryPublisher)
        {
            _telemetryPublisher = telemetryPublisher ?? throw new ArgumentNullException(nameof(telemetryPublisher));
        }

        public string TruthFileName => TruthStorage.TruthFileName;

        public bool AppendTruthLine(string chatId, char role, string text)
        {
            var appended = TruthStorage.AppendTruthLine(chatId, role, text);
            if (!appended)
                return false;

            PublishTelemetry(chatId);
            return true;
        }

        public string GetChatDir(string chatId)
        {
            return TruthStorage.GetChatDir(chatId);
        }

        public string GetTruthPath(string chatId)
        {
            return TruthStorage.GetTruthPath(chatId);
        }

        public string EnsureChatDir(string chatId)
        {
            return TruthStorage.EnsureChatDir(chatId);
        }

        public bool TryBeginTruthRebuild(string chatId, bool backupExisting, out string backupPath, out string tempTruthPath, CancellationToken token)
        {
            return TruthStorage.TryBeginTruthRebuild(chatId, backupExisting, out backupPath, out tempTruthPath, token);
        }

        public void AbortTruthRebuild(string chatId)
        {
            TruthStorage.AbortTruthRebuild(chatId);
        }

        public bool TryCommitTruthRebuild(string chatId)
        {
            return TruthStorage.TryCommitTruthRebuild(chatId);
        }

        private void PublishTelemetry(string chatId)
        {
            if (string.IsNullOrWhiteSpace(chatId))
                return;

            long bytes = 0;
            try
            {
                var truthPath = TruthStorage.GetTruthPath(chatId);
                if (File.Exists(truthPath))
                    bytes = new FileInfo(truthPath).Length;
            }
            catch
            {
                return;
            }

            try
            {
                _telemetryPublisher.PublishTruthBytes(chatId, bytes);
            }
            catch
            {
                // telemetry must never throw
            }
        }
    }
}
