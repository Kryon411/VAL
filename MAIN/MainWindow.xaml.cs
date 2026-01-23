using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using VAL.Host;
using VAL.Host.Services;
using VAL.ViewModels;

namespace VAL
{
    public partial class MainWindow : Window
    {
        private readonly IToastService _toastService;
        private readonly IWebViewRuntime _webViewRuntime;
        private readonly MainWindowViewModel _viewModel;

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        public MainWindow(
            IToastService toastService,
            IWebViewRuntime webViewRuntime,
            MainWindowViewModel viewModel)
        {
            _toastService = toastService;
            _webViewRuntime = webViewRuntime;
            _viewModel = viewModel;

            InitializeComponent();
            DataContext = _viewModel;
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
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
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            }

            await _webViewRuntime.InitializeAsync(WebView);

            WebView.DefaultBackgroundColor = System.Drawing.Color.FromArgb(11, 12, 16);

            try
            {
                await _viewModel.OnLoadedAsync(() => WebView.Focus());
            }
            catch
            {
                ValLog.Warn("MainWindow", "View model initialization failed.");
            }

            WebView.Source = new Uri("https://chatgpt.com");
        }
    }
}
