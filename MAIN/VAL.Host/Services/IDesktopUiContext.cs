using System.Windows;
using System.Windows.Threading;

namespace VAL.Host.Services
{
    public interface IDesktopUiContext
    {
        Dispatcher Dispatcher { get; }

        Window? MainWindow { get; }

        void RegisterMainWindow(Window mainWindow);
    }
}
