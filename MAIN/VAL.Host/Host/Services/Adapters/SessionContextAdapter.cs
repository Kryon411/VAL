using VAL.Host;

namespace VAL.Host.Services.Adapters
{
    public sealed class SessionContextAdapter : ISessionContext
    {
        public void Observe(string type, string? chatId)
        {
            SessionContext.Observe(type, chatId);
        }

        public string? ResolveChatId(string? chatId)
        {
            return SessionContext.ResolveChatId(chatId);
        }

        public bool IsValidChatId(string? chatId)
        {
            return SessionContext.IsValidChatId(chatId);
        }

        public bool IsSessionAttached => SessionContext.IsSessionAttached;
    }
}
