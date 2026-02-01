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
    private static readonly Uri ThemeDictionaryUri =
        new("pack://application:,,,/VAL;component/UI/VALWindowTheme.xaml", UriKind.Absolute);

    private readonly IServiceProvider _services;

    public App(IServiceProvider services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public IServiceProvider Services => _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        EnsureThemeLoaded();

        var mainWindow = _services.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    private static void EnsureThemeLoaded()
    {
        var resources = Current?.Resources;
        if (resources is null)
        {
            return;
        }

        foreach (var dictionary in resources.MergedDictionaries)
        {
            if (dictionary.Source is null)
            {
                continue;
            }

            if (dictionary.Source == ThemeDictionaryUri)
            {
                return;
            }
        }

        resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = ThemeDictionaryUri
        });
    }
}
