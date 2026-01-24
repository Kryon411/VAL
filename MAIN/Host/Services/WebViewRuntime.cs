using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using VAL.Host;
using Microsoft.Extensions.Options;
using VAL.Host.Options;

namespace VAL.Host.Services
{
    public sealed class WebViewRuntime : IWebViewRuntime
    {
        private readonly IAppPaths _appPaths;
        private readonly WebViewOptions _webViewOptions;
        private readonly SemaphoreSlim _initLock = new(1, 1);
        private Task? _initTask;
        private bool _eventsWired;

        public CoreWebView2? Core { get; private set; }

        public WebViewRuntime(IAppPaths appPaths, IOptions<WebViewOptions> webViewOptions)
        {
            _appPaths = appPaths;
            _webViewOptions = webViewOptions.Value;
        }

        public event Action<string>? WebMessageJsonReceived;
        public event Action? NavigationCompleted;

        public async Task InitializeAsync(WebView2 control)
        {
            if (control == null) throw new ArgumentNullException(nameof(control));

            await _initLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_initTask != null)
                {
                    await _initTask;
                    return;
                }

                _initTask = InitializeCoreAsync(control);
            }
            finally
            {
                _initLock.Release();
            }

            await _initTask.ConfigureAwait(false);
        }

        public void PostJson(string json)
        {
            var core = Core;
            if (core == null)
            {
                ValLog.Warn(nameof(WebViewRuntime), "PostJson called before WebView2 initialization.");
                return;
            }

            try
            {
                core.PostWebMessageAsJson(json);
            }
            catch
            {
                ValLog.Warn(nameof(WebViewRuntime), "Failed to post JSON to WebView2.");
            }
        }

        public async Task ExecuteScriptAsync(string js)
        {
            var core = Core;
            if (core == null)
            {
                ValLog.Warn(nameof(WebViewRuntime), "ExecuteScriptAsync called before WebView2 initialization.");
                return;
            }

            try
            {
                await core.ExecuteScriptAsync(js).ConfigureAwait(false);
            }
            catch
            {
                ValLog.Warn(nameof(WebViewRuntime), "Failed to execute script in WebView2.");
            }
        }

        private async Task InitializeCoreAsync(WebView2 control)
        {
            // Profile root (isolated WebView2 user data)
            var userData = _appPaths.ProfileRoot;
            Directory.CreateDirectory(userData);

            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userData);
            await control.EnsureCoreWebView2Async(env);

            Core = control.CoreWebView2;
            Core.Settings.AreDevToolsEnabled = _webViewOptions.EffectiveAllowDevTools;
            Core.Settings.IsStatusBarEnabled = false;
            Core.Settings.IsWebMessageEnabled = true;

            if (!string.IsNullOrWhiteSpace(_webViewOptions.UserAgentOverride))
            {
                try
                {
                    Core.Settings.UserAgent = _webViewOptions.UserAgentOverride;
                }
                catch
                {
                    ValLog.Warn(nameof(WebViewRuntime), "Failed to apply user agent override.");
                }
            }

            if (!_eventsWired)
            {
                Core.NavigationCompleted += (_, __) => NavigationCompleted?.Invoke();
                Core.WebMessageReceived += (_, e) => WebMessageJsonReceived?.Invoke(e.WebMessageAsJson);
                Core.NewWindowRequested += (_, e) => HandleNewWindowRequested(e);
                _eventsWired = true;
            }
        }

        private void HandleNewWindowRequested(CoreWebView2NewWindowRequestedEventArgs e)
        {
            if (!_webViewOptions.BlockNewWindow)
                return;

            try
            {
                e.NewWindow = Core;
                e.Handled = true;
            }
            catch
            {
                try
                {
                    e.Handled = true;

                    var uri = e.Uri;
                    if (!string.IsNullOrWhiteSpace(uri))
                        Core?.Navigate(uri);
                }
                catch
                {
                    // Never let window routing break the host.
                }
            }
        }
    }
}
