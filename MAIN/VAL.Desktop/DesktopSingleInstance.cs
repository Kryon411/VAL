using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VAL
{
    internal sealed class DesktopSingleInstance : IDisposable
    {
        private const string MutexName = "Local\\VAL.Desktop.SingleInstance";
        private const int RestoreWindow = 9;
        private readonly Mutex _mutex;

        private DesktopSingleInstance(Mutex mutex)
        {
            _mutex = mutex;
        }

        public static DesktopSingleInstance? TryAcquire()
        {
            var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
            if (createdNew)
                return new DesktopSingleInstance(mutex);

            mutex.Dispose();
            return null;
        }

        public static void TryActivateExistingWindow()
        {
            try
            {
                using var current = Process.GetCurrentProcess();
                foreach (var process in Process.GetProcessesByName(current.ProcessName))
                {
                    using (process)
                    {
                        if (process.Id == current.Id || process.MainWindowHandle == IntPtr.Zero)
                            continue;

                        ShowWindowAsync(process.MainWindowHandle, RestoreWindow);
                        SetForegroundWindow(process.MainWindowHandle);
                        return;
                    }
                }
            }
            catch
            {
                // Activation is a convenience; the single-instance guarantee still holds.
            }
        }

        public void Dispose()
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            finally
            {
                _mutex.Dispose();
            }
        }

        [DllImport("user32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindowAsync(IntPtr windowHandle, int command);

        [DllImport("user32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr windowHandle);
    }
}
