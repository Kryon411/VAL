using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace VAL.Host.Services
{
    public sealed class UiThread : IUiThread
    {
        private readonly Dispatcher _dispatcher;

        public UiThread()
        {
            _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        }

        public void Invoke(Action action)
        {
            if (action == null)
                return;

            if (_dispatcher.CheckAccess())
            {
                action();
                return;
            }

            _dispatcher.Invoke(action);
        }

        public Task InvokeAsync(Action action)
        {
            if (action == null)
                return Task.CompletedTask;

            return _dispatcher.InvokeAsync(action).Task;
        }

        public IDisposable StartTimer(TimeSpan interval, Action tick)
        {
            var timer = new DispatcherTimer
            {
                Interval = interval
            };

            EventHandler? handler = null;
            handler = (_, __) =>
            {
                try
                {
                    tick?.Invoke();
                }
                catch
                {
                    ValLog.Warn(nameof(UiThread), "UI timer tick failed.");
                }
            };

            timer.Tick += handler;
            timer.Start();

            return new TimerHandle(timer, handler);
        }

        private sealed class TimerHandle : IDisposable
        {
            private readonly DispatcherTimer _timer;
            private readonly EventHandler _handler;

            public TimerHandle(DispatcherTimer timer, EventHandler handler)
            {
                _timer = timer;
                _handler = handler;
            }

            public void Dispose()
            {
                _timer.Tick -= _handler;
                _timer.Stop();
            }
        }
    }
}
