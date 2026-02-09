using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using VAL;
using VAL.Host;
using VAL.Host.Services;

namespace VAL.App.Services
{
    public sealed class CrashHandler : ICrashHandler
    {
        private const string CrashCategory = "Crash";
        private readonly IAppPaths _appPaths;
        private readonly IProcessLauncher _processLauncher;
        private readonly IUiThread _uiThread;
        private readonly SmokeTestSettings _smokeSettings;
        private int _handling;

        public CrashHandler(IAppPaths appPaths, IProcessLauncher processLauncher, IUiThread uiThread, SmokeTestSettings smokeSettings)
        {
            _appPaths = appPaths;
            _processLauncher = processLauncher;
            _uiThread = uiThread;
            _smokeSettings = smokeSettings;
        }

        public void Register(Application application)
        {
            ArgumentNullException.ThrowIfNull(application);

            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                var exception = args.ExceptionObject as Exception ?? new InvalidOperationException("Unhandled exception.");
                HandleCrash(exception, "AppDomain");
            };

            application.DispatcherUnhandledException += (_, args) =>
            {
                HandleCrash(args.Exception, "Dispatcher");
                args.Handled = true;
            };

            TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                HandleCrash(args.Exception, "TaskScheduler");
                args.SetObserved();
            };
        }

        private void HandleCrash(Exception exception, string source)
        {
            if (Interlocked.Exchange(ref _handling, 1) == 1)
                return;

            var details = BuildCrashDetails(exception, source);

            try
            {
                ValLog.Warn(CrashCategory, $"Unhandled exception from {source}.");
                ValLog.Error(CrashCategory, details);
            }
            catch
            {
                // Logging must never throw.
            }

            try
            {
                Directory.CreateDirectory(_appPaths.LogsRoot);
                var reportPath = Path.Combine(_appPaths.LogsRoot, "CrashReport.txt");
                File.WriteAllText(reportPath, details);
            }
            catch
            {
                // Writing crash report must never throw.
            }

            if (_smokeSettings.Enabled)
                return;

            try
            {
                _uiThread.Invoke(() =>
                {
                    try
                    {
                        var dialog = new CrashWindow(details, _appPaths.LogsRoot, _processLauncher);
                        dialog.ShowDialog();
                    }
                    catch
                    {
                        // Do not allow UI crash handling to throw.
                    }
                });
            }
            catch
            {
                // Swallow all crash handler exceptions.
            }
        }

        private static string BuildCrashDetails(Exception exception, string source)
        {
            var timestamp = DateTimeOffset.Now.ToString("u", CultureInfo.InvariantCulture);
            return $"Timestamp: {timestamp}{Environment.NewLine}Source: {source}{Environment.NewLine}{Environment.NewLine}{exception}";
        }
    }
}
