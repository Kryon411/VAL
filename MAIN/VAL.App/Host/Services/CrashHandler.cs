using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

using VAL.Host;

namespace VAL.App.Host.Services
{
    public sealed class CrashHandler : ICrashHandler
    {
        private const string CrashCategory = "Crash";
        private readonly IAppPaths _appPaths;
        private readonly ICrashWindowService _crashWindowService;
        private readonly ILog _log;
        private readonly IUiThread _uiThread;
        private readonly SmokeTestSettings _smokeSettings;
        private int _handling;

        public CrashHandler(
            IAppPaths appPaths,
            ICrashWindowService crashWindowService,
            IUiThread uiThread,
            SmokeTestSettings smokeSettings,
            ILog log)
        {
            _appPaths = appPaths ?? throw new ArgumentNullException(nameof(appPaths));
            _crashWindowService = crashWindowService ?? throw new ArgumentNullException(nameof(crashWindowService));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _uiThread = uiThread ?? throw new ArgumentNullException(nameof(uiThread));
            _smokeSettings = smokeSettings ?? throw new ArgumentNullException(nameof(smokeSettings));
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
                _log.Warn(CrashCategory, $"Unhandled exception from {source}.");
                _log.LogError(CrashCategory, details);
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
                        _crashWindowService.ShowCrash(details, _appPaths.LogsRoot);
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
