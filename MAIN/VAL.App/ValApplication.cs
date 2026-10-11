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

    private readonly IWindowFactory<MainWindow> _mainWindowFactory;
    private readonly IDesktopUiContext _uiContext;

    public ValApplication(IWindowFactory<MainWindow> mainWindowFactory, IDesktopUiContext uiContext)
    {
        _mainWindowFactory = mainWindowFactory ?? throw new ArgumentNullException(nameof(mainWindowFactory));
        _uiContext = uiContext ?? throw new ArgumentNullException(nameof(uiContext));
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        EnsureThemeLoaded();

        // Windows created before Application miss its resource lookup and invalidation lifecycle.
        var mainWindow = _mainWindowFactory.Create();
        _uiContext.RegisterMainWindow(mainWindow);
        MainWindow = mainWindow;

        base.OnStartup(e);
        mainWindow.Show();
    }

    private void EnsureThemeLoaded()
    {
        var resources = Resources;

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
