using System;
using System.Windows;

using VAL.Host;

namespace VAL.App.Host.Services
{
    public sealed class CrashWindowService : ICrashWindowService
    {
        private readonly IWindowFactory<CrashWindow, CrashWindowRequest> _windowFactory;
        private readonly IDesktopUiContext _uiContext;
        private readonly ILog _log;

        public CrashWindowService(
            IWindowFactory<CrashWindow, CrashWindowRequest> windowFactory,
            IDesktopUiContext uiContext,
            ILog log)
        {
            _windowFactory = windowFactory ?? throw new ArgumentNullException(nameof(windowFactory));
            _uiContext = uiContext ?? throw new ArgumentNullException(nameof(uiContext));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public void ShowCrash(string crashDetails, string logsRoot)
        {
            try
            {
                var dialog = _windowFactory.Create(new CrashWindowRequest(
                    crashDetails ?? string.Empty,
                    logsRoot ?? string.Empty));

                var owner = _uiContext.MainWindow;
                if (owner != null && !ReferenceEquals(owner, dialog))
                {
                    dialog.Owner = owner;
                    dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                }

                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                try
                {
                    _log.Warn(nameof(CrashWindowService), $"Failed to show crash dialog: {ex.Message}");
                }
                catch
                {
                    // Logging must never throw while handling a crash.
                }
            }
        }
    }
}
