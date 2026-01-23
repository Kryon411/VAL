using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using VAL;

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
                    _window.Activate();
                    return;
                }

                _window = _serviceProvider.GetRequiredService<DiagnosticsWindow>();
                _window.Owner = Application.Current?.MainWindow;
                _window.Closed += (_, __) => _window = null;
                _window.Show();
            }
            catch
            {
                // Diagnostics must never crash the app.
            }
        }
    }
}
