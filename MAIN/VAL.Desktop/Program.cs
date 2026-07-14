using VAL.App.Hosting;

namespace VAL
{
    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            using var singleInstance = DesktopSingleInstance.TryAcquire();
            if (singleInstance == null)
            {
                DesktopSingleInstance.TryActivateExistingWindow();
                return;
            }

            Environment.ExitCode = ValDesktopBootstrapper.Run(args);
        }
    }
}
