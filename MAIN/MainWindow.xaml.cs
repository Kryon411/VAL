using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Extensions.Options;
using VAL.Host;
using VAL.Host.Options;
using VAL.Host.Services;
using VAL.Host.Startup;
using VAL.Host.WebMessaging;
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
        private ControlCentreOverlay? _ccOverlay;
        private bool _ccLoggedNotReady;
        private bool _isDockOpen;

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const string DockOpenMessage = "{\"type\":\"dock.open\",\"source\":\"host\"}";
        private const string DockCloseMessage = "{\"type\":\"dock.close\",\"source\":\"host\"}";
        private const string DockUiStateSetType = "dock.ui_state.set";

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
            Title = _startupOptions.SafeMode ? "VAL (SAFE MODE)" : "VAL";
            DataContext = _viewModel;
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
            Closed += MainWindow_Closed;
            SourceInitialized += MainWindow_SourceInitialized;
            Activated += MainWindow_Activated;
            Deactivated += MainWindow_Deactivated;
            StateChanged += MainWindow_StateChanged;
            LocationChanged += MainWindow_LocationChanged;
            SizeChanged += MainWindow_SizeChanged;

            _webViewRuntime.WebMessageJsonReceived += _viewModel.HandleWebMessageJson;
            _webViewRuntime.WebMessageJsonReceived += HandleWebMessageForDockState;
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
            _webViewRuntime.WebMessageJsonReceived -= HandleWebMessageForDockState;

            try
            {
                if (_ccOverlay != null)
                {
                    _ccOverlay.ToggleRequested -= ControlCentreOverlay_ToggleRequested;
                    _ccOverlay.Close();
                    _ccOverlay = null;
                }
            }
            catch
            {
                ValLog.Warn("MainWindow", "Failed to close Control Centre overlay window.");
            }
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Maximized;
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

            EnsureControlCentreOverlay();
            ShowControlCentreOverlayIfNeeded();
            UpdateControlCentreOverlayPosition();

            _startupCrashGuard.MarkSuccess();
        }

        private void ControlCentreOverlay_ToggleRequested(object? sender, EventArgs e)
        {
            PostDockStateMessage(_ccLoggedNotReady, value => _ccLoggedNotReady = value);
        }

        private void PostDockStateMessage(bool loggedFlag, Action<bool> setLoggedFlag)
        {
            try
            {
                if (WebView.CoreWebView2 == null)
                {
                    if (loggedFlag)
                    {
                        return;
                    }

                    setLoggedFlag(true);
                    ValLog.Info("MainWindow", "Control Centre toggle ignored because WebView2 is not ready.");
                    return;
                }

                _isDockOpen = !_isDockOpen;
                var payload = _isDockOpen ? DockOpenMessage : DockCloseMessage;
                WebView.CoreWebView2.PostWebMessageAsString(payload);
            }
            catch (Exception ex)
            {
                ValLog.Warn("MainWindow", $"Failed to post Control Centre state message: {ex.Message}");
            }
        }

        private void HandleWebMessageForDockState(WebMessageEnvelope envelope)
        {
            try
            {
                using var document = JsonDocument.Parse(envelope.Json);
                var root = document.RootElement;

                if (!TryReadType(root, out var type) || !string.Equals(type, DockUiStateSetType, StringComparison.Ordinal))
                {
                    return;
                }

                if (TryReadIsOpen(root, out var isOpen))
                {
                    _isDockOpen = isOpen;
                }
            }
            catch
            {
                // Ignore malformed messages from web modules.
            }
        }

        private static bool TryReadType(JsonElement root, out string? type)
        {
            type = null;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (root.TryGetProperty("type", out var directType) && directType.ValueKind == JsonValueKind.String)
            {
                type = directType.GetString();
                return !string.IsNullOrWhiteSpace(type);
            }

            if (root.TryGetProperty("name", out var nameType) && nameType.ValueKind == JsonValueKind.String)
            {
                type = nameType.GetString();
                return !string.IsNullOrWhiteSpace(type);
            }

            return false;
        }

        private static bool TryReadIsOpen(JsonElement root, out bool isOpen)
        {
            isOpen = false;

            if (root.TryGetProperty("isOpen", out var direct) && TryReadBoolean(direct, out isOpen))
            {
                return true;
            }

            if (!root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            return payload.TryGetProperty("isOpen", out var payloadIsOpen) && TryReadBoolean(payloadIsOpen, out isOpen);
        }

        private static bool TryReadBoolean(JsonElement value, out bool result)
        {
            result = false;

            if (value.ValueKind == JsonValueKind.True)
            {
                result = true;
                return true;
            }

            if (value.ValueKind == JsonValueKind.False)
            {
                result = false;
                return true;
            }

            return false;
        }

        private void MainWindow_Activated(object? sender, EventArgs e)
        {
            if (_ccOverlay == null)
            {
                return;
            }

            _ccOverlay.Topmost = true;
            _ccOverlay.Topmost = false;
            UpdateControlCentreOverlayPosition();
        }

        private void MainWindow_Deactivated(object? sender, EventArgs e)
        {
            if (_ccOverlay != null)
            {
                _ccOverlay.Topmost = false;
            }
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                _ccOverlay?.Hide();
                return;
            }

            ShowControlCentreOverlayIfNeeded();
            UpdateControlCentreOverlayPosition();
        }

        private void MainWindow_LocationChanged(object? sender, EventArgs e)
        {
            UpdateControlCentreOverlayPosition();
        }

        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateControlCentreOverlayPosition();
        }

        private void EnsureControlCentreOverlay()
        {
            if (_ccOverlay != null)
            {
                return;
            }

            _ccOverlay = new ControlCentreOverlay();
            _ccOverlay.Owner = this;
            _ccOverlay.ToggleRequested += ControlCentreOverlay_ToggleRequested;
        }

        private void ShowControlCentreOverlayIfNeeded()
        {
            if (_ccOverlay == null || WindowState == WindowState.Minimized)
            {
                return;
            }

            if (_ccOverlay.IsVisible)
            {
                return;
            }

            _ccOverlay.Show();
        }

        private void UpdateControlCentreOverlayPosition()
        {
            if (_ccOverlay == null || !_ccOverlay.IsVisible || WindowState == WindowState.Minimized || WebView.ActualWidth <= 0 || WebView.ActualHeight <= 0)
            {
                return;
            }

            var dpi = VisualTreeHelper.GetDpi(this);
            var webViewOriginPx = WebView.PointToScreen(new Point(0, 0));
            var webViewOriginDip = new Point(webViewOriginPx.X / dpi.DpiScaleX, webViewOriginPx.Y / dpi.DpiScaleY);

            const double rightInset = 16;
            const double topInset = 12;

            var overlayWidth = _ccOverlay.ActualWidth > 0 ? _ccOverlay.ActualWidth : _ccOverlay.Width;
            var targetLeft = webViewOriginDip.X + WebView.ActualWidth - overlayWidth - rightInset;
            var targetTop = webViewOriginDip.Y + topInset;

            _ccOverlay.Left = targetLeft;
            _ccOverlay.Top = targetTop;
        }
    }
}
