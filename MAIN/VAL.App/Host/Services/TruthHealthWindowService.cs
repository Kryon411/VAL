using System;

namespace VAL.App.Host.Services
{
    public sealed class TruthHealthWindowService : ITruthHealthWindowService
    {
        private readonly ModelessWindowHost<TruthHealthWindow> _windowHost;

        public TruthHealthWindowService(
            IWindowFactory<TruthHealthWindow> windowFactory,
            IDesktopUiContext uiContext)
        {
            _windowHost = new ModelessWindowHost<TruthHealthWindow>(windowFactory, uiContext);
        }

        public void ShowTruthHealth()
        {
            try
            {
                _windowHost.Show(ModelessWindowOptions.Default);
            }
            catch
            {
                // Truth health panel must never crash the app.
            }
        }
    }
}
