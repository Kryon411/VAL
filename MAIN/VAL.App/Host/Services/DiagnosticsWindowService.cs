using System;
using System.Windows;

using VAL.Contracts;
using VAL.Host.Commands;

namespace VAL.App.Host.Services
{
    public sealed class DiagnosticsWindowService : IDiagnosticsWindowService
    {
        private readonly ICommandDiagnosticsReporter _diagnosticsReporter;
        private readonly ModelessWindowHost<DiagnosticsWindow> _windowHost;
        private static readonly ModelessWindowOptions WindowOptions = new()
        {
            CenterOwner = true,
            RestoreIfMinimized = true,
            ActivateAfterShow = true
        };

        public DiagnosticsWindowService(
            IWindowFactory<DiagnosticsWindow> windowFactory,
            ICommandDiagnosticsReporter diagnosticsReporter,
            IDesktopUiContext uiContext)
        {
            _windowHost = new ModelessWindowHost<DiagnosticsWindow>(windowFactory, uiContext);
            _diagnosticsReporter = diagnosticsReporter ?? throw new ArgumentNullException(nameof(diagnosticsReporter));
        }

        public void ShowDiagnostics()
        {
            try
            {
                _windowHost.Show(WindowOptions);
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
