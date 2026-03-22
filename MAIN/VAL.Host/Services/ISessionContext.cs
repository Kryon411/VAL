using System;

namespace VAL.Host.Services
{
    public interface ISessionContext
    {
        string ActiveChatId { get; }
        bool IsSessionAttached { get; }
        DateTime LastChatIdUtc { get; }
        void Observe(string? type, string? chatId);
        void SetActiveChatId(string chatId);
        void EnsureInitialized(string? chatId);
        ChatOrigin GetOrigin(string? chatId);
        void MarkContinuumSeeded(string? chatId);
        void MarkChronicleRebuilt(string? chatId);
        bool WasMissingTruthLogAtAttach(string? chatId);
        void SetMissingTruthLogAtAttach(string? chatId, bool missing);
        string ResolveChatId(string? chatId);
        bool IsValidChatId(string? chatId);
    }
}
