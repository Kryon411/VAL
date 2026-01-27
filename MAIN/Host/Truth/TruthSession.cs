using VAL.Host;

namespace VAL.Host.Truth
{
    internal static class TruthSession
    {
        internal static string CurrentChatId => SessionContext.ActiveChatId;

        internal static bool TryGetCurrentChatId(out string chatId)
        {
            chatId = SessionContext.ActiveChatId;
            return SessionContext.IsValidChatId(chatId);
        }
    }
}
