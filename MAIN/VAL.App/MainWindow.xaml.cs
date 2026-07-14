using System;
using System.ComponentModel;
using System.Windows;
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
            IControlCentreOverlayHost overlayHost)
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

            InitializeComponent();
            Title = startupOptions.SafeMode ? "VAL (SAFE MODE)" : "VAL";
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

            _nativeChromeController.LayoutToggleRequested += NativeChromeController_LayoutToggleRequested;
            _overlayHost.Clicked += ControlCentreOverlay_Clicked;
            _overlayHost.GeometryChanged += ControlCentreOverlay_GeometryChanged;
            _overlayHost.LayoutToggleRequested += ControlCentreOverlay_LayoutToggleRequested;
            _webViewRuntime.WebMessageJsonReceived += _viewModel.HandleWebMessageJson;
            _webViewRuntime.WebMessageJsonReceived += HandleWebMessageForDockState;
            _webViewRuntime.NavigationCompleted += WebViewRuntime_NavigationCompleted;
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
        }

        private void MainWindow_SourceInitialized(object? sender, EventArgs e)
        {
            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                _viewModel.AttachPortalWindow(hwnd);
                _nativeChromeController.Attach(hwnd);
            }
            catch
            {
                _log.Warn("MainWindow", "Failed to attach portal window handle.");
            }
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            _nativeChromeController.LayoutToggleRequested -= NativeChromeController_LayoutToggleRequested;
            _overlayHost.Clicked -= ControlCentreOverlay_Clicked;
            _overlayHost.GeometryChanged -= ControlCentreOverlay_GeometryChanged;
            _overlayHost.LayoutToggleRequested -= ControlCentreOverlay_LayoutToggleRequested;
            _webViewRuntime.WebMessageJsonReceived -= _viewModel.HandleWebMessageJson;
            _webViewRuntime.WebMessageJsonReceived -= HandleWebMessageForDockState;
            _webViewRuntime.NavigationCompleted -= WebViewRuntime_NavigationCompleted;
            _shellTimingController.FlushAndStop();

            _nativeChromeController.Detach();
            _overlayController.Close();
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Maximized;
            _toastService.Initialize(this);
            _shellTimingController.LoadState();
            _nativeChromeController.ApplyImmersiveDarkMode();

            var webViewHost = new MainWindowWebViewHost(WebView, _webViewRuntime);
            await _startupCoordinator.InitializeAsync(
                webViewHost,
                () => _viewModel.OnLoadedAsync(webViewHost.Focus));

            _overlayController.Attach(this);
            _overlayController.InitializeGeometry(BuildDefaultOverlayGeometry);
            _overlayController.HandleOwnerStateChanged(WindowState);
            ApplyLayoutMode();

            _shellTimingController.RequestDockStateSync();
        }

        private void ControlCentreOverlay_Clicked(object? sender, EventArgs e)
        {
            if (!_shellBridgeController.TryHandleLauncherClick(DateTime.UtcNow, out var requiresDockStateSync))
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
            if (_shellBridgeController.TryHandleDockMessage(envelope, GetVirtualScreenBounds()))
            {
                _shellTimingController.ScheduleStatePersist();
            }
        }

        private void MainWindow_Activated(object? sender, EventArgs e)
        {
            _nativeChromeController.RegisterLayoutHotKey();
            _overlayController.HandleActivated();
        }

        private void MainWindow_Deactivated(object? sender, EventArgs e)
        {
            _nativeChromeController.UnregisterLayoutHotKey();
            _overlayController.HandleDeactivated();
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            _overlayController.HandleOwnerStateChanged(WindowState);
        }

        private void WebViewRuntime_NavigationCompleted()
        {
            _ = Dispatcher.InvokeAsync(() =>
            {
                try
                {
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
            _overlayController.HandleOwnerBoundsChanged();
        }

        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            _overlayController.HandleOwnerBoundsChanged();
        }

        private GeometryState BuildDefaultOverlayGeometry()
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            var webViewOriginPx = WebView.PointToScreen(new Point(0, 0));
            return ControlCentreOverlayPlacement.CreateDefault(
                webViewOriginPx,
                WebView.ActualWidth,
                dpi.DpiScaleX,
                dpi.DpiScaleY);
        }

        private void ToggleLayoutMode()
        {
            _ = _shellStateController.ToggleLayoutMode();
            ApplyLayoutMode();
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

        private static Rect GetVirtualScreenBounds()
        {
            return new Rect(
                SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth,
                SystemParameters.VirtualScreenHeight);
        }

    }
}
