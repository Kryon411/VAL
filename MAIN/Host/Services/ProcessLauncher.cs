using System.Diagnostics;

namespace VAL.Host.Services
{
    public sealed class ProcessLauncher : IProcessLauncher
    {
        public void OpenFolder(string path)
        {
            OpenPath(path);
        }

        public void OpenUrl(string url)
        {
            OpenPath(url);
        }

        public void OpenPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch
            {
                ValLog.Warn(nameof(ProcessLauncher), "Failed to open path.");
            }
        }
    }
}
