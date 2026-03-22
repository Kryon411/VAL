using System;
using System.Windows;
using System.Windows.Threading;

namespace VAL.Host.Services
{
    public sealed class DesktopUiContext : IDesktopUiContext
    {
        public DesktopUiContext(Dispatcher dispatcher)
        {
            Dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        public Dispatcher Dispatcher { get; }

        public Window? MainWindow { get; private set; }

        public void RegisterMainWindow(Window mainWindow)
        {
            MainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
        }
    }
}
