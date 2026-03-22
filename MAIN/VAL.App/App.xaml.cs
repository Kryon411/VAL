using System;
using System.Windows;
using VAL.Host.Services;

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
        new("pack://application:,,,/VAL.App;component/UI/VALWindowTheme.xaml", UriKind.Absolute);

    private readonly MainWindow _mainWindow;
    private readonly IDesktopUiContext _uiContext;

    public App(MainWindow mainWindow, IDesktopUiContext uiContext)
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
