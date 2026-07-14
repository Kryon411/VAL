using System;
using System.Threading.Tasks;

using Microsoft.Extensions.Options;

using VAL.Host;
using VAL.Host.Options;
using VAL.Host.Startup;

namespace VAL.App.Host.Services
{
    public sealed class MainWindowStartupCoordinator
    {
        private static readonly System.Drawing.Color DefaultWebViewBackgroundColor = System.Drawing.Color.FromArgb(11, 12, 16);
        private readonly WebViewOptions _webViewOptions;
        private readonly IStartupCrashGuard _startupCrashGuard;
        private readonly IDesktopDialogService _dialogService;
        private readonly ILog _log;

        public MainWindowStartupCoordinator(
            IOptions<WebViewOptions> webViewOptions,
            IStartupCrashGuard startupCrashGuard,
            IDesktopDialogService dialogService,
            ILog log)
        {
            _webViewOptions = webViewOptions?.Value ?? throw new ArgumentNullException(nameof(webViewOptions));
            _startupCrashGuard = startupCrashGuard ?? throw new ArgumentNullException(nameof(startupCrashGuard));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public async Task InitializeAsync(
            IMainWindowWebViewHost webViewHost,
            Func<Task> initializeViewModelAsync)
        {
            ArgumentNullException.ThrowIfNull(webViewHost);
            ArgumentNullException.ThrowIfNull(initializeViewModelAsync);

            var webViewInitialized = await TryInitializeWebViewAsync(webViewHost);
            await TryInitializeViewModelAsync(initializeViewModelAsync);

            if (webViewInitialized)
            {
                webViewHost.Navigate(ResolveStartUri());
            }

            _startupCrashGuard.MarkSuccess();
        }

        private async Task<bool> TryInitializeWebViewAsync(
            IMainWindowWebViewHost webViewHost)
        {
            try
            {
                await webViewHost.InitializeAsync();
                webViewHost.ApplyDefaultBackgroundColor(DefaultWebViewBackgroundColor);
                return true;
            }
            catch (Exception ex)
            {
                _log.Warn(nameof(MainWindowStartupCoordinator), $"WebView2 initialization failed: {ex}");

                try
                {
                    _dialogService.ShowError(
                        "VAL could not initialize the embedded browser. Please restart the app.",
                        "VAL");
                }
                catch
                {
                    _log.Warn(nameof(MainWindowStartupCoordinator), "Failed to show WebView2 initialization error dialog.");
                }

                return false;
            }
        }

        private async Task TryInitializeViewModelAsync(Func<Task> initializeViewModelAsync)
        {
            try
            {
                await initializeViewModelAsync();
            }
            catch
            {
                _log.Warn(nameof(MainWindowStartupCoordinator), "View model initialization failed.");
            }
        }

        private Uri ResolveStartUri()
        {
            if (!Uri.TryCreate(_webViewOptions.StartUrl, UriKind.Absolute, out var startUri))
            {
                _log.Warn(nameof(MainWindowStartupCoordinator), "Invalid StartUrl configured. Falling back to default.");
                startUri = new Uri(WebViewOptions.DefaultStartUrl);
            }

            return startUri;
        }
    }
}
