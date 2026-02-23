using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
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
        private bool _layoutMode;
        private bool _overlayMovedByUser;
        private DateTime _lastLauncherClickUtc = DateTime.MinValue;
        private HwndSource? _hwndSource;
        private UiState _uiState = UiState.Default;
        private readonly DispatcherTimer _stateWriteTimer;
        private bool _layoutHotKeyRegistered;

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int WM_HOTKEY = 0x0312;
        private const int HOTKEY_ID_LAYOUT_MODE = 0x4C415956;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint VK_L = 0x4C;
        private const string DockOpenMessage = "{\"type\":\"dock.open\",\"source\":\"host\"}";
        private const string DockCloseMessage = "{\"type\":\"dock.close\",\"source\":\"host\"}";
        private const string DockLayoutEnable = "{\"type\":\"dock.layout.enable\",\"source\":\"host\"}";
        private const string DockLayoutDisable = "{\"type\":\"dock.layout.disable\",\"source\":\"host\"}";
        private const string DockStateType = "dock.state";
        private const string DockUiStateSetType = "dock.ui_state.set";
        private const string DockUiStateDataType = "dock.ui_state.data";
        private static readonly JsonSerializerOptions CachedJsonOptions = new(JsonSerializerDefaults.Web);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

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

            _stateWriteTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(150)
            };
            _stateWriteTimer.Tick += (_, _) =>
            {
                _stateWriteTimer.Stop();
                SaveUiState();
            };

            InitializeComponent();
            _uiState = LoadUiState();
            _layoutMode = _uiState.LayoutMode;
            _isDockOpen = _uiState.Dock.IsOpen;
            _overlayMovedByUser = _uiState.ControlCentre.HasPosition;

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

                _hwndSource = HwndSource.FromHwnd(hwnd);
                _hwndSource?.AddHook(MainWindowWndProc);
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

            UnregisterLayoutHotKey();

            if (_hwndSource != null)
            {
                _hwndSource.RemoveHook(MainWindowWndProc);
                _hwndSource = null;
            }

            try
            {
                if (_ccOverlay != null)
                {
                    _ccOverlay.LauncherClicked -= ControlCentreOverlay_LauncherClicked;
                    _ccOverlay.GeometryCommitted -= ControlCentreOverlay_GeometryCommitted;
                    _ccOverlay.Close();
                    _ccOverlay = null;
                }
            }
            catch
            {
                ValLog.Warn("MainWindow", "Failed to close Control Centre overlay window.");
            }

            SaveUiState();
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
            ApplyLayoutMode(_layoutMode);
            PostDockLayoutMode();
            PostDockUiStateData();

            _startupCrashGuard.MarkSuccess();
        }

        private void ControlCentreOverlay_LauncherClicked(object? sender, EventArgs e)
        {
            if ((DateTime.UtcNow - _lastLauncherClickUtc).TotalMilliseconds < 250)
            {
                return;
            }

            _lastLauncherClickUtc = DateTime.UtcNow;
            PostDockStateMessage(_ccLoggedNotReady, value => _ccLoggedNotReady = value);
        }

        private void ControlCentreOverlay_GeometryCommitted(object? sender, Rect bounds)
        {
            if (!_layoutMode)
            {
                return;
            }

            _overlayMovedByUser = true;
            var clamped = ClampToVirtualScreen(bounds);
            _uiState.ControlCentre = Geometry.FromRect(clamped, _uiState.ControlCentre);
            QueueStateSave();
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
                    ValLog.Info("MainWindow", "Control Centre command ignored because WebView2 is not ready.");
                    return;
                }

                PostDockUiStateData();
                var nextIsOpen = !_isDockOpen;
                _isDockOpen = nextIsOpen;
                _uiState.Dock.IsOpen = nextIsOpen;
                var payload = nextIsOpen ? DockOpenMessage : DockCloseMessage;
                WebView.CoreWebView2.PostWebMessageAsString(payload);
                QueueStateSave();
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

                if (!TryReadType(root, out var type))
                {
                    return;
                }

                if (string.Equals(type, DockStateType, StringComparison.Ordinal) && TryReadIsOpen(root, out var dockOpen))
                {
                    _isDockOpen = dockOpen;
                    _uiState.Dock.IsOpen = dockOpen;
                    QueueStateSave();
                    return;
                }

                if (!string.Equals(type, DockUiStateSetType, StringComparison.Ordinal))
                {
                    return;
                }

                if (TryReadNumber(root, "x", out var x)) _uiState.Dock.X = x;
                if (TryReadNumber(root, "y", out var y)) _uiState.Dock.Y = y;
                if (TryReadNumber(root, "w", out var w)) _uiState.Dock.W = w;
                if (TryReadNumber(root, "h", out var h)) _uiState.Dock.H = h;
                if (TryReadIsOpen(root, out var isOpen))
                {
                    _isDockOpen = isOpen;
                    _uiState.Dock.IsOpen = isOpen;
                }

                QueueStateSave();
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

            string? directTypeValue = null;
            if (root.TryGetProperty("type", out var directType) && directType.ValueKind == JsonValueKind.String)
            {
                directTypeValue = directType.GetString();
            }

            if (root.TryGetProperty("name", out var nameType) && nameType.ValueKind == JsonValueKind.String)
            {
                var envelopeName = nameType.GetString();
                if (!string.IsNullOrWhiteSpace(envelopeName)
                    && string.Equals(directTypeValue, "command", StringComparison.OrdinalIgnoreCase))
                {
                    type = envelopeName;
                    return true;
                }
            }

            if (!string.IsNullOrWhiteSpace(directTypeValue))
            {
                type = directTypeValue;
                return true;
            }

            if (root.TryGetProperty("name", out var fallbackName) && fallbackName.ValueKind == JsonValueKind.String)
            {
                type = fallbackName.GetString();
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

        private static bool TryReadNumber(JsonElement root, string name, out double value)
        {
            value = 0;

            if (root.TryGetProperty(name, out var direct) && TryGetNumber(direct, out value))
            {
                return true;
            }

            if (root.TryGetProperty("payload", out var payload)
                && payload.ValueKind == JsonValueKind.Object
                && payload.TryGetProperty(name, out var nested)
                && TryGetNumber(nested, out value))
            {
                return true;
            }

            return false;
        }

        private static bool TryGetNumber(JsonElement value, out double result)
        {
            result = 0;
            if (value.ValueKind == JsonValueKind.Number)
            {
                return value.TryGetDouble(out result);
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                return double.TryParse(value.GetString(), out result);
            }

            return false;
        }

        private void MainWindow_Activated(object? sender, EventArgs e)
        {
            RegisterLayoutHotKey();

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
            UnregisterLayoutHotKey();

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
            _ccOverlay.LauncherClicked += ControlCentreOverlay_LauncherClicked;
            _ccOverlay.GeometryCommitted += ControlCentreOverlay_GeometryCommitted;
            _ccOverlay.SetLayoutMode(_layoutMode);

            var ccRect = ClampToVirtualScreen(_uiState.ControlCentre.ToRect(defaultX: 0, defaultY: 0, defaultW: 30, defaultH: 30));
            _ccOverlay.ApplyGeometry(ccRect.Left, ccRect.Top, ccRect.Width, ccRect.Height);
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

            if (_layoutMode && _overlayMovedByUser)
            {
                return;
            }

            var dpi = VisualTreeHelper.GetDpi(this);
            var webViewOriginPx = WebView.PointToScreen(new Point(0, 0));
            var webViewOriginDip = new Point(webViewOriginPx.X / dpi.DpiScaleX, webViewOriginPx.Y / dpi.DpiScaleY);

            const double rightInset = 16;
            const double topInset = 12;

            var overlayWidth = _ccOverlay.Width > 0 ? _ccOverlay.Width : 30;
            var overlayHeight = _ccOverlay.Height > 0 ? _ccOverlay.Height : 30;
            var targetLeft = webViewOriginDip.X + WebView.ActualWidth - overlayWidth - rightInset;
            var targetTop = webViewOriginDip.Y + topInset;
            var clamped = ClampToVirtualScreen(new Rect(targetLeft, targetTop, overlayWidth, overlayHeight));

            _ccOverlay.Left = clamped.Left;
            _ccOverlay.Top = clamped.Top;
            _uiState.ControlCentre = Geometry.FromRect(clamped, _uiState.ControlCentre);
        }

        private void ApplyLayoutMode(bool enabled)
        {
            _layoutMode = enabled;
            _uiState.LayoutMode = enabled;
            _ccOverlay?.SetLayoutMode(enabled);
            if (!enabled)
            {
                _overlayMovedByUser = false;
                UpdateControlCentreOverlayPosition();
            }

            QueueStateSave();
        }

        private void ToggleLayoutMode()
        {
            ApplyLayoutMode(!_layoutMode);
            PostDockLayoutMode();
        }

        private void PostDockLayoutMode()
        {
            if (WebView.CoreWebView2 == null)
            {
                return;
            }

            WebView.CoreWebView2.PostWebMessageAsString(_layoutMode ? DockLayoutEnable : DockLayoutDisable);
        }

        private void PostDockUiStateData()
        {
            if (WebView.CoreWebView2 == null)
            {
                return;
            }

            double? x = _uiState.Dock.X;
            double? y = _uiState.Dock.Y;
            var width = (_uiState.Dock.W.HasValue && _uiState.Dock.W.Value > 1) ? _uiState.Dock.W : 560d;
            var height = (_uiState.Dock.H.HasValue && _uiState.Dock.H.Value > 1) ? _uiState.Dock.H : 460d;

            if (_uiState.Dock.HasPosition)
            {
                var dockRect = ClampToVirtualScreen(_uiState.Dock.ToRect(defaultX: _uiState.Dock.X ?? double.NaN, defaultY: _uiState.Dock.Y ?? double.NaN, defaultW: width ?? 560d, defaultH: height ?? 460d));
                _uiState.Dock = Geometry.FromRect(dockRect, _uiState.Dock);
                x = _uiState.Dock.X;
                y = _uiState.Dock.Y;
                width = _uiState.Dock.W;
                height = _uiState.Dock.H;
            }

            var payload = JsonSerializer.Serialize(new
            {
                type = DockUiStateDataType,
                source = "host",
                x,
                y,
                w = width,
                h = height,
                isOpen = _uiState.Dock.IsOpen
            }, CachedJsonOptions);

            WebView.CoreWebView2.PostWebMessageAsString(payload);
        }

        private IntPtr MainWindowWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != WM_HOTKEY || wParam.ToInt32() != HOTKEY_ID_LAYOUT_MODE)
            {
                return IntPtr.Zero;
            }

            ToggleLayoutMode();
            handled = true;
            return IntPtr.Zero;
        }

        private void RegisterLayoutHotKey()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            if (_layoutHotKeyRegistered)
            {
                return;
            }

            var registered = RegisterHotKey(hwnd, HOTKEY_ID_LAYOUT_MODE, MOD_CONTROL | MOD_ALT | MOD_SHIFT, VK_L);
            if (!registered)
            {
                var err = Marshal.GetLastWin32Error();
                ValLog.Warn("MainWindow", $"RegisterHotKey failed for layout mode ({err}).");
                return;
            }

            _layoutHotKeyRegistered = true;
        }

        private void UnregisterLayoutHotKey()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            if (!_layoutHotKeyRegistered)
            {
                return;
            }

            if (!UnregisterHotKey(hwnd, HOTKEY_ID_LAYOUT_MODE))
            {
                var err = Marshal.GetLastWin32Error();
                ValLog.Warn("MainWindow", $"UnregisterHotKey failed for layout mode ({err}).");
            }

            _layoutHotKeyRegistered = false;
        }

        private void QueueStateSave()
        {
            _stateWriteTimer.Stop();
            _stateWriteTimer.Start();
        }

        private UiState LoadUiState()
        {
            try
            {
                var path = GetUiStatePath();
                if (!File.Exists(path))
                {
                    return UiState.Default;
                }

                var json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<UiState>(json, CachedJsonOptions);
                return loaded?.Normalize() ?? UiState.Default;
            }
            catch
            {
                return UiState.Default;
            }
        }

        private void SaveUiState()
        {
            try
            {
                if (_ccOverlay != null)
                {
                    _uiState.ControlCentre = Geometry.FromRect(new Rect(_ccOverlay.Left, _ccOverlay.Top, _ccOverlay.Width, _ccOverlay.Height), _uiState.ControlCentre);
                }

                _uiState.Dock = Geometry.FromRect(ClampToVirtualScreen(_uiState.Dock.ToRect(double.NaN, double.NaN, 560, 460)), _uiState.Dock);
                _uiState.Version = 1;
                _uiState = _uiState.Normalize();

                var path = GetUiStatePath();
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(_uiState, CachedJsonOptions);
                File.WriteAllText(path, json);
            }
            catch
            {
                ValLog.Warn("MainWindow", "Failed to save control centre UI state.");
            }
        }

        private static string GetUiStatePath()
        {
            return Path.Combine(AppContext.BaseDirectory, "State", "controlcentre.ui.json");
        }

        private static Rect ClampToVirtualScreen(Rect rect)
        {
            var screenLeft = SystemParameters.VirtualScreenLeft;
            var screenTop = SystemParameters.VirtualScreenTop;
            var screenWidth = SystemParameters.VirtualScreenWidth;
            var screenHeight = SystemParameters.VirtualScreenHeight;

            if (double.IsNaN(rect.X) || double.IsInfinity(rect.X))
            {
                rect.X = screenLeft + 16;
            }

            if (double.IsNaN(rect.Y) || double.IsInfinity(rect.Y))
            {
                rect.Y = screenTop + 16;
            }

            rect.Width = double.IsNaN(rect.Width) || rect.Width <= 1 ? 30 : rect.Width;
            rect.Height = double.IsNaN(rect.Height) || rect.Height <= 1 ? 30 : rect.Height;

            var maxLeft = screenLeft + Math.Max(0, screenWidth - rect.Width);
            var maxTop = screenTop + Math.Max(0, screenHeight - rect.Height);
            rect.X = Math.Min(Math.Max(rect.X, screenLeft), maxLeft);
            rect.Y = Math.Min(Math.Max(rect.Y, screenTop), maxTop);
            return rect;
        }

        private sealed class UiState
        {
            public int Version { get; set; } = 1;
            public Geometry ControlCentre { get; set; } = new();
            public Geometry Dock { get; set; } = new();
            public bool LayoutMode { get; set; }

            public static UiState Default => new()
            {
                Version = 1,
                ControlCentre = new Geometry { X = null, Y = null, W = 30, H = 30 },
                Dock = new Geometry { X = null, Y = null, W = 560, H = 460 },
                LayoutMode = false
            };

            public UiState Normalize()
            {
                ControlCentre ??= new Geometry();
                Dock ??= new Geometry();
                return this;
            }
        }

        private sealed class Geometry
        {
            public double? X { get; set; }
            public double? Y { get; set; }
            public double? W { get; set; }
            public double? H { get; set; }
            public bool IsOpen { get; set; }

            public bool HasPosition => X.HasValue && Y.HasValue;

            public Rect ToRect(double defaultX, double defaultY, double defaultW, double defaultH)
            {
                var x = X ?? defaultX;
                var y = Y ?? defaultY;
                var w = (W.HasValue && W.Value > 1) ? W.Value : defaultW;
                var h = (H.HasValue && H.Value > 1) ? H.Value : defaultH;
                return new Rect(x, y, w, h);
            }

            public static Geometry FromRect(Rect rect, Geometry current)
            {
                current.X = rect.X;
                current.Y = rect.Y;
                current.W = rect.Width;
                current.H = rect.Height;
                return current;
            }
        }
    }
}
