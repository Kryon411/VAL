using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using VAL;
using VAL.Host.Commands;

namespace VAL.Host.Services
{
    public sealed class DiagnosticsWindowService : IDiagnosticsWindowService
    {
        private readonly IServiceProvider _serviceProvider;
        private DiagnosticsWindow? _window;

        public DiagnosticsWindowService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
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

                _window = _serviceProvider.GetRequiredService<DiagnosticsWindow>();
                _window.Owner = Application.Current?.MainWindow;
                _window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                _window.Closed += (_, __) => _window = null;
                _window.Show();
                _window.Activate();
            }
            catch (Exception ex)
            {
                // Diagnostics must never crash the app.
                ToolsCommandHandlers.ReportDiagnosticsFailure(null, ex, "exception");
            }
        }
    }
}
