namespace VAL.Host.Services
{
    public interface IProcessLauncher
    {
        void OpenFolder(string path);
        void OpenUrl(string url);
        void OpenPath(string path);
    }
}
