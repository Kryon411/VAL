using System;
using System.Windows;

using VAL.Host.Services;

namespace VAL.App;

/// <summary>
/// Code-only WPF Application shell.
///
/// Startup runs through the desktop bootstrapper and DI host, so the application shell
/// stays code-only and loads shared theme resources programmatically.
/// </summary>
public sealed class ValApplication : Application
{
    private static readonly Uri ThemeDictionaryUri =
        new("pack://application:,,,/VAL.App;component/UI/VALWindowTheme.xaml", UriKind.Absolute);

    private readonly MainWindow _mainWindow;
    private readonly IDesktopUiContext _uiContext;

    public ValApplication(MainWindow mainWindow, IDesktopUiContext uiContext)
    {
        _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
        _uiContext = uiContext ?? throw new ArgumentNullException(nameof(uiContext));
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        EnsureThemeLoaded();

        _uiContext.RegisterMainWindow(_mainWindow);
        MainWindow = _mainWindow;
        _mainWindow.Show();
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
