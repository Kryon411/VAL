using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using VAL.Host.Services;
using VAL.UI.Truth;

namespace VAL.App.Services
{
    public sealed class TruthHealthWindowService : ITruthHealthWindowService
    {
        private readonly IServiceProvider _serviceProvider;
        private TruthHealthWindow? _window;

        public TruthHealthWindowService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public void ShowTruthHealth()
        {
            try
            {
                if (_window != null)
                {
                    _window.Activate();
                    return;
                }

                _window = _serviceProvider.GetRequiredService<TruthHealthWindow>();
                _window.Owner = Application.Current?.MainWindow;
                _window.Closed += (_, __) => _window = null;
                _window.Show();
            }
            catch
            {
                // Truth health panel must never crash the app.
            }
        }
    }
}
