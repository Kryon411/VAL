namespace VAL.App.Host.Services
{
    public interface ICrashWindowService
    {
        void ShowCrash(string crashDetails, string logsRoot);
    }
}
