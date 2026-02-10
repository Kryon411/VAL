namespace VAL.Host.Services
{
    public interface IAppPaths
    {
        string ContentRoot { get; }
        string DataRoot { get; }
        string LogsRoot { get; }
        string ModulesRoot { get; }
        string ProfileRoot { get; }
    }
}
