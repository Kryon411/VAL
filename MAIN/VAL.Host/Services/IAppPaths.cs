namespace VAL.Host.Services
{
    public interface IAppPaths
    {
        string ContentRoot { get; }
        string ProductRoot { get; }
        string StateRoot { get; }
        string DataRoot { get; }
        string LogsRoot { get; }
        string ModulesRoot { get; }
        string MemoryChatsRoot { get; }
        string ProfileRoot { get; }
    }
}
