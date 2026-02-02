using System.Threading;

namespace VAL.Continuum.Pipeline.Truth
{
    public sealed class ContinuumWriter : IContinuumWriter
    {
        public void AppendTruthLine(string chatId, char role, string text)
        {
            TruthStorage.AppendTruthLine(chatId, role, text);
        }

        public string GetTruthPath(string chatId)
        {
            return TruthStorage.GetTruthPath(chatId);
        }

        public string EnsureChatDir(string chatId)
        {
            return TruthStorage.EnsureChatDir(chatId);
        }

        public bool TryBeginTruthRebuild(string chatId, CancellationToken token, bool backupExisting, out string backupPath, out string tempTruthPath)
        {
            return TruthStorage.TryBeginTruthRebuild(chatId, token, backupExisting, out backupPath, out tempTruthPath);
        }

        public void AbortTruthRebuild(string chatId)
        {
            TruthStorage.AbortTruthRebuild(chatId);
        }

        public bool TryCommitTruthRebuild(string chatId)
        {
            return TruthStorage.TryCommitTruthRebuild(chatId);
        }
    }
}
