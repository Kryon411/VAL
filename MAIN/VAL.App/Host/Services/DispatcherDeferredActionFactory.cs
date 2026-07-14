using System;
using System.Windows.Threading;

using VAL.Host.Services;

namespace VAL.App.Host.Services
{
    public sealed class DispatcherDeferredActionFactory : IDeferredActionFactory
    {
        private readonly IDesktopUiContext _uiContext;

        public DispatcherDeferredActionFactory(IDesktopUiContext uiContext)
        {
            _uiContext = uiContext ?? throw new ArgumentNullException(nameof(uiContext));
        }

        public IDeferredAction Create(TimeSpan interval, Action callback)
        {
            ArgumentNullException.ThrowIfNull(callback);
            return new DispatcherDeferredAction(_uiContext.Dispatcher, interval, callback);
        }

        private sealed class DispatcherDeferredAction : IDeferredAction
        {
            private readonly DispatcherTimer _timer;
            private readonly Action _callback;

            public DispatcherDeferredAction(Dispatcher dispatcher, TimeSpan interval, Action callback)
            {
                ArgumentNullException.ThrowIfNull(dispatcher);
                _callback = callback ?? throw new ArgumentNullException(nameof(callback));
                _timer = new DispatcherTimer(interval, DispatcherPriority.Normal, OnTick, dispatcher);
                _timer.Stop();
            }

            public void Restart()
            {
                _timer.Stop();
                _timer.Start();
            }

            public void Cancel()
            {
                _timer.Stop();
            }

            private void OnTick(object? sender, EventArgs e)
            {
                _timer.Stop();
                _callback();
            }
        }
    }
}
