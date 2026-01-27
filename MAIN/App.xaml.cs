using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace VAL;

/// <summary>
/// Code-only WPF Application shell.
///
/// Phase 1 currently uses Program.Main as the single entry point (Host + DI composition root).
/// In that model, App.xaml is optional; we intentionally do NOT call InitializeComponent here
/// so the app does not depend on XAML-generated code-behind.
/// </summary>
public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    private readonly IServiceProvider _services;

    public App(IServiceProvider services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        Services = _services;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var mainWindow = _services.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();
    }
}
