using System;
using System.Windows;

namespace VAL.App.Host.Services;

public sealed class DesktopShutdownService : IDesktopShutdownService
{
    public void Shutdown()
    {
        var app = Application.Current ?? throw new InvalidOperationException("VAL is not running.");
        app.Dispatcher.BeginInvoke(new Action(() => app.Shutdown()));
    }
}
