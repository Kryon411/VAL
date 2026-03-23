using System.Threading;

namespace VAL.Continuum.Pipeline.Truth
{
    public interface ITruthStore
    {
        string TruthFileName { get; }
        bool AppendTruthLine(string chatId, char role, string text);
        string GetChatDir(string chatId);
        string GetTruthPath(string chatId);
        string EnsureChatDir(string chatId);
        bool TryBeginTruthRebuild(string chatId, bool backupExisting, out string backupPath, out string tempTruthPath, CancellationToken token);
        void AbortTruthRebuild(string chatId);
        bool TryCommitTruthRebuild(string chatId);
    }
}
