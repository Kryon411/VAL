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
        private readonly DispatcherTimer _persistTimer;
        private readonly DispatcherTimer _dockInitStateTimer;

        private ControlCentreOverlay? _ccOverlay;
        private bool _ccLoggedNotReady;
        private bool _isDockOpen;
        private bool _layoutMode;
        private DateTime _lastLauncherClickUtc = DateTime.MinValue;
        private bool _hotKeyRegistered;
        private HwndSource? _hwndSource;
        private ControlCentreUiState _uiState = ControlCentreUiState.Default;

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int HotKeyIdLayoutToggle = 0x4256;
        private const uint ModAlt = 0x0001;
        private const uint ModControl = 0x0002;
        private const uint ModShift = 0x0004;
        private const int VKeyL = 0x4C;
        private const int WmHotKey = 0x0312;

        private const string DockOpenMessage = "{\"type\":\"dock.open\",\"source\":\"host\"}";
        private const string DockCloseMessage = "{\"type\":\"dock.close\",\"source\":\"host\"}";
        private const string DockLayoutEnableMessage = "{\"type\":\"dock.layout.enable\",\"source\":\"host\"}";
        private const string DockLayoutDisableMessage = "{\"type\":\"dock.layout.disable\",\"source\":\"host\"}";
        private const string DockUiStateSetType = "dock.ui_state.set";
        private const string DockStateType = "dock.state";
        private static readonly JsonSerializerOptions UiStateJsonOptions = new() { WriteIndented = true };

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
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

            _persistTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _persistTimer.Tick += PersistTimer_Tick;

            _dockInitStateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
            _dockInitStateTimer.Tick += (_, _) =>
            {
                _dockInitStateTimer.Stop();
                SendDockUiStateData();
            };

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
                if (hwnd != IntPtr.Zero)
                {
                    _hwndSource = HwndSource.FromHwnd(hwnd);
                    _hwndSource?.AddHook(WndProc);
                }
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
                _hwndSource.RemoveHook(WndProc);
                _hwndSource = null;
            }

            try
            {
                if (_ccOverlay != null)
                {
                    _ccOverlay.Clicked -= ControlCentreOverlay_Clicked;
                    _ccOverlay.GeometryChanged -= ControlCentreOverlay_GeometryChanged;
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
            LoadUiState();
            _layoutMode = _uiState.LayoutMode;

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
            ApplyOverlayGeometry();
            ApplyLayoutMode();

            _dockInitStateTimer.Start();
            _startupCrashGuard.MarkSuccess();
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WmHotKey && wParam.ToInt32() == HotKeyIdLayoutToggle)
            {
                ToggleLayoutMode();
                handled = true;
            }

            return IntPtr.Zero;
        }

        private void ControlCentreOverlay_Clicked(object? sender, EventArgs e)
        {
            if ((DateTime.UtcNow - _lastLauncherClickUtc).TotalMilliseconds < 250)
            {
                return;
            }

            _lastLauncherClickUtc = DateTime.UtcNow;

            if (_isDockOpen)
            {
                PostDockMessage(DockCloseMessage);
            }
            else
            {
                PostDockMessage(DockOpenMessage);
                _dockInitStateTimer.Start();
            }
        }

        private void ControlCentreOverlay_GeometryChanged(object? sender, EventArgs e)
        {
            if (_ccOverlay == null)
            {
                return;
            }

            ClampOverlayToVirtualScreen();
            _uiState.ControlCentre = new GeometryState(_ccOverlay.Left, _ccOverlay.Top, _ccOverlay.Width, _ccOverlay.Height);
            ScheduleStatePersist();
        }

        private void PostDockMessage(string payload)
        {
            try
            {
                if (WebView.CoreWebView2 == null)
                {
                    if (_ccLoggedNotReady)
                    {
                        return;
                    }

                    _ccLoggedNotReady = true;
                    ValLog.Info("MainWindow", "Control Centre click ignored because WebView2 is not ready.");
                    return;
                }

                WebView.CoreWebView2.PostWebMessageAsString(payload);
            }
            catch (Exception ex)
            {
                ValLog.Warn("MainWindow", $"Failed to post Control Centre message: {ex.Message}");
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

                if (string.Equals(type, DockStateType, StringComparison.Ordinal) && TryReadIsOpen(root, out var dockIsOpen))
                {
                    _isDockOpen = dockIsOpen;
                    _uiState.Dock.IsOpen = dockIsOpen;
                    ScheduleStatePersist();
                    return;
                }

                if (!string.Equals(type, DockUiStateSetType, StringComparison.Ordinal))
                {
                    return;
                }

                if (TryReadIsOpen(root, out var isOpen))
                {
                    _isDockOpen = isOpen;
                    _uiState.Dock.IsOpen = isOpen;
                }

                UpdateDockGeometryFromMessage(root);
                ScheduleStatePersist();
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

        private static double? TryReadDoubleProperty(JsonElement root, string name)
        {
            if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
            {
                return number;
            }

            if (root.TryGetProperty("payload", out var payload) && payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(name, out var payloadValue) && payloadValue.ValueKind == JsonValueKind.Number && payloadValue.TryGetDouble(out var payloadNumber))
            {
                return payloadNumber;
            }

            return null;
        }

        private void UpdateDockGeometryFromMessage(JsonElement root)
        {
            var x = TryReadDoubleProperty(root, "x");
            var y = TryReadDoubleProperty(root, "y");
            var w = TryReadDoubleProperty(root, "w");
            var h = TryReadDoubleProperty(root, "h");

            if (x.HasValue) _uiState.Dock.X = x.Value;
            if (y.HasValue) _uiState.Dock.Y = y.Value;
            if (w.HasValue) _uiState.Dock.W = w.Value;
            if (h.HasValue) _uiState.Dock.H = h.Value;

            ClampDockGeometry();
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
            if (!_layoutMode)
            {
                ClampOverlayToVirtualScreen();
            }
        }

        private void MainWindow_LocationChanged(object? sender, EventArgs e)
        {
            if (!_layoutMode)
            {
                ClampOverlayToVirtualScreen();
            }
        }

        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!_layoutMode)
            {
                ClampOverlayToVirtualScreen();
            }
        }

        private void EnsureControlCentreOverlay()
        {
            if (_ccOverlay != null)
            {
                return;
            }

            _ccOverlay = new ControlCentreOverlay();
            _ccOverlay.Owner = this;
            _ccOverlay.Clicked += ControlCentreOverlay_Clicked;
            _ccOverlay.GeometryChanged += ControlCentreOverlay_GeometryChanged;
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

        private void ApplyOverlayGeometry()
        {
            if (_ccOverlay == null)
            {
                return;
            }

            var geometry = _uiState.ControlCentre;
            if (!geometry.HasPosition)
            {
                geometry = BuildDefaultOverlayGeometry();
                _uiState.ControlCentre = geometry;
            }

            _ccOverlay.Width = geometry.W;
            _ccOverlay.Height = geometry.H;
            _ccOverlay.Left = geometry.X;
            _ccOverlay.Top = geometry.Y;

            ClampOverlayToVirtualScreen();
        }

        private GeometryState BuildDefaultOverlayGeometry()
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            var webViewOriginPx = WebView.PointToScreen(new Point(0, 0));
            var webViewOriginDip = new Point(webViewOriginPx.X / dpi.DpiScaleX, webViewOriginPx.Y / dpi.DpiScaleY);
            var w = 36d;
            var h = 36d;
            var x = webViewOriginDip.X + Math.Max(0, WebView.ActualWidth - w - 16);
            var y = webViewOriginDip.Y + 12;
            return new GeometryState(x, y, w, h);
        }

        private void ToggleLayoutMode()
        {
            _layoutMode = !_layoutMode;
            _uiState.LayoutMode = _layoutMode;
            ApplyLayoutMode();
            ScheduleStatePersist();
        }

        private void ApplyLayoutMode()
        {
            if (_ccOverlay != null)
            {
                _ccOverlay.LayoutModeEnabled = _layoutMode;
            }

            PostDockMessage(_layoutMode ? DockLayoutEnableMessage : DockLayoutDisableMessage);
            SendDockUiStateData();
        }

        private void RegisterLayoutHotKey()
        {
            if (_hotKeyRegistered)
            {
                return;
            }

            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            var registered = RegisterHotKey(hwnd, HotKeyIdLayoutToggle, ModControl | ModAlt | ModShift, VKeyL);
            if (!registered)
            {
                ValLog.Warn("MainWindow", "Failed to register layout hotkey Ctrl+Alt+Shift+L.");
                return;
            }

            _hotKeyRegistered = true;
        }

        private void UnregisterLayoutHotKey()
        {
            if (!_hotKeyRegistered)
            {
                return;
            }

            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                var unregistered = UnregisterHotKey(hwnd, HotKeyIdLayoutToggle);
                if (!unregistered)
                {
                    ValLog.Warn("MainWindow", "Failed to unregister layout hotkey Ctrl+Alt+Shift+L.");
                }
            }

            _hotKeyRegistered = false;
        }

        private void SendDockUiStateData()
        {
            var dock = _uiState.Dock;
            var payload = JsonSerializer.Serialize(new
            {
                type = "dock.ui_state.data",
                source = "host",
                x = dock.X,
                y = dock.Y,
                w = dock.W,
                h = dock.H,
                isOpen = dock.IsOpen
            });

            PostDockMessage(payload);
        }

        private void ClampOverlayToVirtualScreen()
        {
            if (_ccOverlay == null)
            {
                return;
            }

            var geometry = _uiState.ControlCentre;
            ClampWindowGeometry(_ccOverlay, ref geometry);
            _uiState.ControlCentre = geometry;
        }

        private void ClampDockGeometry()
        {
            var dock = _uiState.Dock;
            var virtualLeft = SystemParameters.VirtualScreenLeft;
            var virtualTop = SystemParameters.VirtualScreenTop;
            var virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
            var virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;

            dock.W = Math.Clamp(dock.W, 360, Math.Max(360, SystemParameters.VirtualScreenWidth));
            dock.H = Math.Clamp(dock.H, 180, Math.Max(180, SystemParameters.VirtualScreenHeight));
            dock.X = Math.Clamp(dock.X, virtualLeft, virtualRight - dock.W);
            dock.Y = Math.Clamp(dock.Y, virtualTop, virtualBottom - dock.H);
        }

        private static void ClampWindowGeometry(Window window, ref GeometryState state)
        {
            var left = SystemParameters.VirtualScreenLeft;
            var top = SystemParameters.VirtualScreenTop;
            var right = left + SystemParameters.VirtualScreenWidth;
            var bottom = top + SystemParameters.VirtualScreenHeight;

            state.W = Math.Clamp(state.W, window.MinWidth, window.MaxWidth > 0 ? window.MaxWidth : state.W);
            state.H = Math.Clamp(state.H, window.MinHeight, window.MaxHeight > 0 ? window.MaxHeight : state.H);
            state.X = Math.Clamp(state.X, left, right - state.W);
            state.Y = Math.Clamp(state.Y, top, bottom - state.H);

            window.Width = state.W;
            window.Height = state.H;
            window.Left = state.X;
            window.Top = state.Y;
        }

        private void ScheduleStatePersist()
        {
            _persistTimer.Stop();
            _persistTimer.Start();
        }

        private void PersistTimer_Tick(object? sender, EventArgs e)
        {
            _persistTimer.Stop();
            SaveUiState();
        }

        private void LoadUiState()
        {
            var path = GetUiStatePath();
            try
            {
                if (!File.Exists(path))
                {
                    _uiState = ControlCentreUiState.Default;
                    return;
                }

                var json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<ControlCentreUiState>(json, UiStateJsonOptions);
                _uiState = loaded?.Normalize() ?? ControlCentreUiState.Default;
            }
            catch
            {
                _uiState = ControlCentreUiState.Default;
            }
        }

        private void SaveUiState()
        {
            try
            {
                var path = GetUiStatePath();
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(_uiState.Normalize(), UiStateJsonOptions);
                File.WriteAllText(path, json);
            }
            catch
            {
                ValLog.Warn("MainWindow", "Failed to persist control centre layout state.");
            }
        }

        private static string GetUiStatePath()
        {
            return Path.Combine(AppContext.BaseDirectory, "State", "controlcentre.ui.json");
        }

        private sealed class ControlCentreUiState
        {
            public int Version { get; set; } = 1;
            public GeometryState ControlCentre { get; set; } = new(0, 0, 36, 36);
            public DockGeometryState Dock { get; set; } = new();
            public bool LayoutMode { get; set; }

            public static ControlCentreUiState Default => new();

            public ControlCentreUiState Normalize()
            {
                Version = 1;
                if (ControlCentre.W <= 0 || ControlCentre.H <= 0)
                {
                    ControlCentre = new GeometryState(ControlCentre.X, ControlCentre.Y, 36, 36);
                }

                Dock.W = Dock.W <= 0 ? 560 : Dock.W;
                Dock.H = Dock.H <= 0 ? 460 : Dock.H;
                return this;
            }
        }

        private sealed class DockGeometryState
        {
            public double X { get; set; } = 72;
            public double Y { get; set; } = 56;
            public double W { get; set; } = 560;
            public double H { get; set; } = 460;
            public bool IsOpen { get; set; }
        }

        private struct GeometryState
        {
            public GeometryState(double x, double y, double w, double h)
            {
                X = x;
                Y = y;
                W = w;
                H = h;
            }

            public double X { get; set; }
            public double Y { get; set; }
            public double W { get; set; }
            public double H { get; set; }
            public bool HasPosition => W > 0 && H > 0;
        }
    }
}
