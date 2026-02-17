using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Options;
using VAL.Host;
using VAL.Host.Services;
using VAL.Host.Options;
using VAL.Host.Startup;
using VAL.ViewModels;

namespace VAL
{
    public partial class MainWindow : Window
    {
        private readonly IToastService _toastService;
        private readonly IWebViewRuntime _webViewRuntime;
        private readonly MainWindowViewModel _viewModel;
        private readonly WebViewOptions _webViewOptions;
        private readonly StartupOptions _startupOptions;
        private readonly StartupCrashGuard _startupCrashGuard;
        private bool _loggedWebViewUnavailable;

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const string DockToggleMessage = "{\"type\":\"dock.toggle\",\"source\":\"host\"}";

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        public MainWindow(
            IToastService toastService,
            IWebViewRuntime webViewRuntime,
            MainWindowViewModel viewModel,
            IOptions<WebViewOptions> webViewOptions,
            StartupOptions startupOptions,
            StartupCrashGuard startupCrashGuard)
        {
            _toastService = toastService;
            _webViewRuntime = webViewRuntime;
            _viewModel = viewModel;
            _webViewOptions = webViewOptions.Value;
            _startupOptions = startupOptions;
            _startupCrashGuard = startupCrashGuard;

            InitializeComponent();
            InitializeControlCentreButtonIcon();
            Title = _startupOptions.SafeMode ? "VAL (SAFE MODE)" : "VAL";
            DataContext = _viewModel;
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
            Closed += MainWindow_Closed;
            SourceInitialized += MainWindow_SourceInitialized;

            _webViewRuntime.WebMessageJsonReceived += _viewModel.HandleWebMessageJson;
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            try
            {
                if (_viewModel.ShouldCancelClose(() =>
                    MessageBox.Show(
                        "An operation is still running. Exiting may interrupt it.",
                        "VAL",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning
                    ) == MessageBoxResult.Yes))
                {
                    e.Cancel = true;
                }
            }
            catch
            {
                // Never block close due to a guardrail failure.
                ValLog.Warn("MainWindow", "Close guard failed.");
            }
        }

        private void MainWindow_SourceInitialized(object? sender, EventArgs e)
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                _viewModel.AttachPortalWindow(hwnd);
            }
            catch
            {
                ValLog.Warn("MainWindow", "Failed to attach portal window handle.");
            }
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            _webViewRuntime.WebMessageJsonReceived -= _viewModel.HandleWebMessageJson;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Always launch maximized
            WindowState = WindowState.Maximized;

            // ToastHub centralizes toast policy/gating, ToastManager remains the renderer.
            _toastService.Initialize(this);

            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                int dark = 1;
                var hr = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
                if (hr != 0)
                {
                    ValLog.Warn("MainWindow", $"Failed to set dark mode attribute (HRESULT=0x{hr:X8}).");
                }
            }

            var webViewInitialized = false;
            try
            {
                await _webViewRuntime.InitializeAsync(WebView);
                webViewInitialized = true;
            }
            catch (Exception ex)
            {
                ValLog.Warn("MainWindow", $"WebView2 initialization failed: {ex}");
                MessageBox.Show(
                    "VAL could not initialize the embedded browser. Please restart the app.",
                    "VAL",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }

            if (webViewInitialized)
            {
                WebView.DefaultBackgroundColor = System.Drawing.Color.FromArgb(11, 12, 16);
            }

            try
            {
                await _viewModel.OnLoadedAsync(() => WebView.Focus());
            }
            catch
            {
                ValLog.Warn("MainWindow", "View model initialization failed.");
            }

            if (!Uri.TryCreate(_webViewOptions.StartUrl, UriKind.Absolute, out var startUri))
            {
                ValLog.Warn("MainWindow", "Invalid StartUrl configured. Falling back to default.");
                startUri = new Uri(WebViewOptions.DefaultStartUrl);
            }

            if (webViewInitialized)
            {
                WebView.Source = startUri;
            }

            _startupCrashGuard.MarkSuccess();
        }

        private void ControlCentreButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (WebView.CoreWebView2 == null)
                {
                    if (_loggedWebViewUnavailable)
                    {
                        return;
                    }

                    _loggedWebViewUnavailable = true;
                    ValLog.Info("MainWindow", "Control Centre toggle ignored because WebView2 is not ready.");
                    return;
                }

                WebView.CoreWebView2.PostWebMessageAsString(DockToggleMessage);
            }
            catch (Exception ex)
            {
                ValLog.Warn("MainWindow", $"Failed to post Control Centre toggle message: {ex.Message}");
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ToggleMaxRestore();
                return;
            }

            DragMove();
        }

        private void WindowMinimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void WindowMaxRestore_Click(object sender, RoutedEventArgs e)
        {
            ToggleMaxRestore();
        }

        private void WindowClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ToggleMaxRestore()
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        private void InitializeControlCentreButtonIcon()
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Icons", "VAL_Blue_Lens.ico");
            var imageSource = TryLoadIcon(iconPath);

            ControlCentreButton.ApplyTemplate();
            var buttonImage = ControlCentreButton.Template.FindName("ControlCentreButtonImage", ControlCentreButton) as Image;
            var fallbackText = ControlCentreButton.Template.FindName("ControlCentreFallbackText", ControlCentreButton) as TextBlock;

            if (imageSource != null)
            {
                if (buttonImage != null)
                {
                    buttonImage.Source = imageSource;
                    buttonImage.Visibility = Visibility.Visible;
                }

                if (fallbackText != null)
                {
                    fallbackText.Visibility = Visibility.Collapsed;
                }

                return;
            }

            if (buttonImage != null)
            {
                buttonImage.Visibility = Visibility.Collapsed;
            }

            if (fallbackText != null)
            {
                fallbackText.Visibility = Visibility.Visible;
            }
        }

        private static BitmapFrame? TryLoadIcon(string iconPath)
        {
            try
            {
                if (!File.Exists(iconPath))
                {
                    return null;
                }

                var uri = new Uri(iconPath, UriKind.Absolute);
                var bitmap = BitmapFrame.Create(uri, BitmapCreateOptions.DelayCreation, BitmapCacheOption.OnLoad);
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }
    }
}
