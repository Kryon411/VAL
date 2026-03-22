using System;
using VAL.UI.Truth;

namespace VAL.Host.Services
{
    public sealed class TruthHealthWindowService : ITruthHealthWindowService
    {
        private readonly IWindowFactory<TruthHealthWindow> _windowFactory;
        private readonly IDesktopUiContext _uiContext;
        private TruthHealthWindow? _window;

        public TruthHealthWindowService(
            IWindowFactory<TruthHealthWindow> windowFactory,
            IDesktopUiContext uiContext)
        {
            _windowFactory = windowFactory;
            _uiContext = uiContext;
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

                _window = _windowFactory.Create();
                _window.Owner = _uiContext.MainWindow;
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
