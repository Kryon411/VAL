using System.Threading;

namespace VAL.Continuum.Pipeline.Truth
{
    public interface IContinuumWriter
    {
        void AppendTruthLine(string chatId, char role, string text);
        string GetTruthPath(string chatId);
        string EnsureChatDir(string chatId);
        bool TryBeginTruthRebuild(string chatId, CancellationToken token, bool backupExisting, out string backupPath, out string tempTruthPath);
        void AbortTruthRebuild(string chatId);
        bool TryCommitTruthRebuild(string chatId);
    }
}
