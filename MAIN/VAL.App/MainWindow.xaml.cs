using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

using VAL.Host;
using VAL.Host.Services;
using VAL.Host.Startup;
using VAL.Host.WebMessaging;

namespace VAL.App
{
    public partial class MainWindow : Window
    {
        private readonly ILog _log;
        private readonly IDesktopDialogService _dialogService;
        private readonly IToastService _toastService;
        private readonly IWebViewRuntime _webViewRuntime;
        private readonly MainWindowViewModel _viewModel;
        private readonly MainWindowStartupCoordinator _startupCoordinator;
        private readonly MainWindowShellStateController _shellStateController;
        private readonly MainWindowShellBridgeController _shellBridgeController;
        private readonly MainWindowShellTimingController _shellTimingController;
        private readonly MainWindowOverlayController _overlayController;
        private readonly MainWindowNativeChromeController _nativeChromeController;
        private readonly IControlCentreOverlayHost _overlayHost;
        private readonly NavigationRecoveryOverlayHost _recoveryOverlayHost;
        private Rect _headerLauncher = Rect.Empty;
        private bool _closed;

        internal NavigationRecoveryOverlay? NativeRecoveryOverlay => _recoveryOverlayHost.Overlay;

        public MainWindow(
            ILog log,
            IDesktopDialogService dialogService,
            IToastService toastService,
            IWebViewRuntime webViewRuntime,
            MainWindowViewModel viewModel,
            StartupOptions startupOptions,
            MainWindowStartupCoordinator startupCoordinator,
            MainWindowShellStateController shellStateController,
            MainWindowShellBridgeController shellBridgeController,
            MainWindowShellTimingController shellTimingController,
            MainWindowOverlayController overlayController,
            MainWindowNativeChromeController nativeChromeController,
            IControlCentreOverlayHost overlayHost,
            NavigationRecoveryOverlayHost recoveryOverlayHost)
        {
            ArgumentNullException.ThrowIfNull(startupOptions);

            _log = log ?? throw new ArgumentNullException(nameof(log));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _toastService = toastService;
            _webViewRuntime = webViewRuntime;
            _viewModel = viewModel;
            _startupCoordinator = startupCoordinator ?? throw new ArgumentNullException(nameof(startupCoordinator));
            _shellStateController = shellStateController ?? throw new ArgumentNullException(nameof(shellStateController));
            _shellBridgeController = shellBridgeController ?? throw new ArgumentNullException(nameof(shellBridgeController));
            _shellTimingController = shellTimingController ?? throw new ArgumentNullException(nameof(shellTimingController));
            _overlayController = overlayController ?? throw new ArgumentNullException(nameof(overlayController));
            _nativeChromeController = nativeChromeController ?? throw new ArgumentNullException(nameof(nativeChromeController));
            _overlayHost = overlayHost ?? throw new ArgumentNullException(nameof(overlayHost));
            _recoveryOverlayHost = recoveryOverlayHost ?? throw new ArgumentNullException(nameof(recoveryOverlayHost));

            InitializeComponent();
            Title = startupOptions.SafeMode ? "VAL (SAFE MODE)" : "VAL";
            if (startupOptions.ValidationSession) Title += " (VALIDATION - ISOLATED DATA)";
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
            PreviewKeyDown += MainWindow_PreviewKeyDown;

            _nativeChromeController.LayoutToggleRequested += NativeChromeController_LayoutToggleRequested;
            _overlayHost.Clicked += ControlCentreOverlay_Clicked;
            _overlayHost.GeometryChanged += ControlCentreOverlay_GeometryChanged;
            _overlayHost.LayoutToggleRequested += ControlCentreOverlay_LayoutToggleRequested;
            _overlayHost.BrowserFocusRequested += NativeControl_BrowserFocusRequested;
            _recoveryOverlayHost.BrowserFocusRequested += NativeControl_BrowserFocusRequested;
            _webViewRuntime.WebMessageJsonReceived += _viewModel.HandleWebMessageJson;
            _webViewRuntime.WebMessageJsonReceived += HandleWebMessageForDockState;
            _webViewRuntime.NavigationCompleted += WebViewRuntime_NavigationCompleted;
            _webViewRuntime.NavigationStateChanged += WebViewRuntime_NavigationStateChanged;
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            try
            {
                if (_viewModel.ShouldCancelClose(() =>
                    _dialogService.ConfirmWarning(
                        "An operation is still running. Exiting may interrupt it.",
                        "VAL")))
                {
                    e.Cancel = true;
                }
            }
            catch
            {
                _log.Warn("MainWindow", "Close guard failed.");
            }
            finally
            {
                _recoveryOverlayHost.PrepareOwnerClose(e.Cancel);
            }
        }

        private void MainWindow_SourceInitialized(object? sender, EventArgs e)
        {
            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                _viewModel.AttachPortalWindow(hwnd);
                _nativeChromeController.Attach(hwnd);
                _recoveryOverlayHost.Attach(this);
            }
            catch
            {
                _log.Warn("MainWindow", "Failed to attach portal window handle.");
            }
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            _closed = true;
            PreviewKeyDown -= MainWindow_PreviewKeyDown;
            _nativeChromeController.LayoutToggleRequested -= NativeChromeController_LayoutToggleRequested;
            _overlayHost.Clicked -= ControlCentreOverlay_Clicked;
            _overlayHost.GeometryChanged -= ControlCentreOverlay_GeometryChanged;
            _overlayHost.LayoutToggleRequested -= ControlCentreOverlay_LayoutToggleRequested;
            _overlayHost.BrowserFocusRequested -= NativeControl_BrowserFocusRequested;
            _recoveryOverlayHost.BrowserFocusRequested -= NativeControl_BrowserFocusRequested;
            _webViewRuntime.WebMessageJsonReceived -= _viewModel.HandleWebMessageJson;
            _webViewRuntime.WebMessageJsonReceived -= HandleWebMessageForDockState;
            _webViewRuntime.NavigationCompleted -= WebViewRuntime_NavigationCompleted;
            _webViewRuntime.NavigationStateChanged -= WebViewRuntime_NavigationStateChanged;
            _shellTimingController.FlushAndStop();

            _nativeChromeController.Detach();
            _overlayController.Close();
            _recoveryOverlayHost.Close();
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Maximized;
            RefreshRecoveryOverlay();
            _toastService.Initialize(this);
            _shellTimingController.LoadState();
            _nativeChromeController.ApplyImmersiveDarkMode();

            var webViewHost = new MainWindowWebViewHost(WebView, _webViewRuntime);
            await _startupCoordinator.InitializeAsync(
                webViewHost,
                () => _viewModel.OnLoadedAsync(webViewHost.Focus));

            _viewModel.RefreshNavigationAvailability();
            _overlayController.Attach(this);
            _overlayController.InitializeGeometry(BuildDefaultOverlayGeometry, BuildWebViewBounds);
            _overlayController.HandleOwnerStateChanged(WindowState);
            ApplyLayoutMode();

            _shellTimingController.RequestDockStateSync();
            if (IsActive) WebView.Focus();
        }

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (!NativeControlKeyboardNavigation.IsFocusCycle(key, Keyboard.Modifiers) || !_webViewRuntime.IsReady)
                return;

            e.Handled = true;
            if (e.IsRepeat) return;
            // WebView2 forwards accelerators synchronously; defer native focus until its callback returns.
            _ = Dispatcher.InvokeAsync(() =>
            {
                if (_closed || !IsActive || WindowState == WindowState.Minimized) return;
                if (_webViewRuntime.IsBridgeReady) _overlayHost.TryFocus();
                else _recoveryOverlayHost.TryFocus();
            });
        }

        private void NativeControl_BrowserFocusRequested(object? sender, EventArgs e)
        {
            if (_closed || WindowState == WindowState.Minimized) return;
            Activate();
            WebView.Focus();
        }

        private void ControlCentreOverlay_Clicked(object? sender, EventArgs e)
        {
            if (!_shellBridgeController.TryHandleLauncherClick(DateTime.UtcNow, out var requiresDockStateSync, () =>
            {
                Activate();
                WebView.Focus();
            }))
            {
                return;
            }

            if (requiresDockStateSync)
            {
                _shellTimingController.RequestDockStateSync();
            }

            _shellTimingController.ScheduleStatePersist();
        }

        private void ControlCentreOverlay_GeometryChanged(object? sender, EventArgs e)
        {
            _overlayController.HandleGeometryChanged();
            _shellTimingController.ScheduleStatePersist();
        }

        private void HandleWebMessageForDockState(WebMessageEnvelope envelope)
        {
            if (!_webViewRuntime.IsBridgeReady) return;
            if (ControlCentreHeaderLayout.TryRead(envelope, out var launcher))
            {
                _headerLauncher = launcher;
                _overlayController.HandleOwnerBoundsChanged();
                return;
            }
            if (_shellBridgeController.TryHandleDockMessage(envelope,
                new Rect(0, 0, WebView.ActualWidth / WebView.ZoomFactor, WebView.ActualHeight / WebView.ZoomFactor)))
            {
                if (!_shellStateController.IsControlCentreCustom)
                    _overlayController.HandleOwnerBoundsChanged();
                _shellTimingController.ScheduleStatePersist();
            }
        }

        private void MainWindow_Activated(object? sender, EventArgs e)
        {
            RefreshLayoutHotKey();
            _overlayController.HandleActivated();
            RefreshRecoveryOverlay();
        }

        private void MainWindow_Deactivated(object? sender, EventArgs e)
        {
            _nativeChromeController.UnregisterLayoutHotKey();
            _overlayController.HandleDeactivated();
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            _overlayController.HandleOwnerStateChanged(WindowState);
            RefreshRecoveryOverlay();
        }

        private void WebViewRuntime_NavigationStateChanged()
        {
            _ = Dispatcher.InvokeAsync(() =>
            {
                if (!_webViewRuntime.IsBridgeReady) _headerLauncher = Rect.Empty;
                _viewModel.RefreshNavigationAvailability();
                _overlayController.HandleNavigationStateChanged(WindowState);
                RefreshLayoutHotKey();
                RefreshRecoveryOverlay();
            });
        }

        private void WebViewRuntime_NavigationCompleted()
        {
            _ = Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    _viewModel.RefreshNavigationAvailability();
                    RefreshRecoveryOverlay();
                    if (_overlayController.HandleNavigationCompleted(WindowState, IsActive))
                    {
                        _shellTimingController.RequestDockStateSync();
                    }
                }
                catch
                {
                    _log.Warn("MainWindow", "Failed to refresh overlay state after navigation.");
                }
            });
        }

        private void MainWindow_LocationChanged(object? sender, EventArgs e)
        {
            if (WindowState != WindowState.Minimized) _overlayController.HandleOwnerBoundsChanged();
            RefreshRecoveryOverlay();
        }

        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (WindowState != WindowState.Minimized) _overlayController.HandleOwnerBoundsChanged();
            RefreshRecoveryOverlay();
        }

        private void RefreshRecoveryOverlay()
        {
            if (!IsLoaded || WindowState == WindowState.Minimized)
            {
                _recoveryOverlayHost.Refresh(Rect.Empty);
                return;
            }

            var dpi = VisualTreeHelper.GetDpi(this);
            var origin = WebView.PointToScreen(new Point(0, 0));
            _recoveryOverlayHost.Refresh(new Rect(
                origin.X / dpi.DpiScaleX, origin.Y / dpi.DpiScaleY,
                WebView.ActualWidth, WebView.ActualHeight));
        }

        private GeometryState BuildDefaultOverlayGeometry()
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            var webViewOriginPx = WebView.PointToScreen(new Point(0, 0));
            if (!_headerLauncher.IsEmpty)
            {
                var scale = WebView.ZoomFactor;
                return new GeometryState(
                    webViewOriginPx.X / dpi.DpiScaleX + _headerLauncher.X * scale,
                    webViewOriginPx.Y / dpi.DpiScaleY + _headerLauncher.Y * scale,
                    Math.Clamp(_headerLauncher.Width * scale, 34, 64),
                    Math.Clamp(_headerLauncher.Height * scale, 34, 64));
            }
            return ControlCentreOverlayPlacement.CreateDefault(
                webViewOriginPx,
                WebView.ActualWidth,
                dpi.DpiScaleX,
                dpi.DpiScaleY);
        }

        private Rect BuildWebViewBounds()
        {
            if (!IsLoaded || WindowState == WindowState.Minimized) return Rect.Empty;
            var dpi = VisualTreeHelper.GetDpi(this);
            var origin = WebView.PointToScreen(new Point());
            return new Rect(origin.X / dpi.DpiScaleX, origin.Y / dpi.DpiScaleY,
                WebView.ActualWidth, WebView.ActualHeight);
        }

        private void ToggleLayoutMode()
        {
            if (!_webViewRuntime.IsBridgeReady) return;
            _shellStateController.ResetPlacement();
            _overlayController.HandleOwnerBoundsChanged();
            _shellBridgeController.SendDockUiState();
            _shellTimingController.ScheduleStatePersist();
        }

        private void ControlCentreOverlay_LayoutToggleRequested(object? sender, EventArgs e)
        {
            ToggleLayoutMode();
        }

        private void NativeChromeController_LayoutToggleRequested(object? sender, EventArgs e)
        {
            ToggleLayoutMode();
        }

        private void ApplyLayoutMode()
        {
            _overlayController.ApplyLayoutMode();
            _shellBridgeController.PublishLayoutMode();
        }

        private void RefreshLayoutHotKey()
        {
            if (_webViewRuntime.IsBridgeReady && IsActive)
                _nativeChromeController.RegisterLayoutHotKey();
            else
                _nativeChromeController.UnregisterLayoutHotKey();
        }

    }
}
