using System;
using System.Windows;

namespace VAL.App.Host.Services
{
    internal sealed class ModelessWindowOptions
    {
        public static readonly ModelessWindowOptions Default = new();

        public bool CenterOwner { get; init; }
        public bool RestoreIfMinimized { get; init; } = true;
        public bool ActivateAfterShow { get; init; } = true;
    }

    internal sealed class ModelessWindowHost<TWindow>
        where TWindow : Window
    {
        private readonly IWindowFactory<TWindow> _windowFactory;
        private readonly IDesktopUiContext _uiContext;
        private TWindow? _window;

        public ModelessWindowHost(
            IWindowFactory<TWindow> windowFactory,
            IDesktopUiContext uiContext)
        {
            _windowFactory = windowFactory ?? throw new ArgumentNullException(nameof(windowFactory));
            _uiContext = uiContext ?? throw new ArgumentNullException(nameof(uiContext));
        }

        public void Show(ModelessWindowOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            if (_window != null)
            {
                if (options.RestoreIfMinimized && _window.WindowState == WindowState.Minimized)
                    _window.WindowState = WindowState.Normal;

                _window.Activate();
                return;
            }

            _window = _windowFactory.Create();
            if (options.CenterOwner)
            {
                _window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }

            var owner = _uiContext.MainWindow;
            if (owner != null && !ReferenceEquals(owner, _window))
            {
                _window.Owner = owner;
            }

            _window.Closed += (_, __) => _window = null;
            _window.Show();

            if (options.ActivateAfterShow)
            {
                _window.Activate();
            }
        }
    }
}
