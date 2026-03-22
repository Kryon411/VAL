using System;
using System.Windows;
using VAL;
using VAL.Contracts;
using VAL.Host.Commands;

namespace VAL.Host.Services
{
    public sealed class DiagnosticsWindowService : IDiagnosticsWindowService
    {
        private readonly IWindowFactory<DiagnosticsWindow> _windowFactory;
        private readonly ICommandDiagnosticsReporter _diagnosticsReporter;
        private readonly IDesktopUiContext _uiContext;
        private DiagnosticsWindow? _window;

        public DiagnosticsWindowService(
            IWindowFactory<DiagnosticsWindow> windowFactory,
            ICommandDiagnosticsReporter diagnosticsReporter,
            IDesktopUiContext uiContext)
        {
            _windowFactory = windowFactory;
            _diagnosticsReporter = diagnosticsReporter;
            _uiContext = uiContext;
        }

        public void ShowDiagnostics()
        {
            try
            {
                if (_window != null)
                {
                    if (_window.WindowState == WindowState.Minimized)
                        _window.WindowState = WindowState.Normal;
                    _window.Activate();
                    return;
                }

                _window = _windowFactory.Create();
                _window.Owner = _uiContext.MainWindow;
                _window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                _window.Closed += (_, __) => _window = null;
                _window.Show();
                _window.Activate();
            }
            catch (Exception ex)
            {
                // Diagnostics must never crash the app.
                _diagnosticsReporter.ReportDiagnosticsFailure(
                    null,
                    ex,
                    "exception");
            }
        }
    }
}
