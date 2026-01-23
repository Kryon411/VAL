namespace VAL.Host.Services
{
    public interface ISessionContext
    {
        void Observe(string type, string? chatId);
        string? ResolveChatId(string? chatId);
        bool IsValidChatId(string? chatId);
        bool IsSessionAttached { get; }
    }
}
