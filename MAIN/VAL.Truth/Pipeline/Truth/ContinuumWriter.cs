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
    }
}
