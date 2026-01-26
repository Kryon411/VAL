using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using VAL.Host;
using Microsoft.Extensions.Options;
using VAL.Host.Options;
using VAL.Host.Security;
using VAL.Host.WebMessaging;

namespace VAL.Host.Services
{
    public sealed class WebViewRuntime : IWebViewRuntime
    {
        private readonly IAppPaths _appPaths;
        private readonly WebViewOptions _webViewOptions;
        private readonly SemaphoreSlim _initLock = new(1, 1);
        private Task? _initTask;
        private bool _eventsWired;
        private volatile bool _bridgeArmed;
        private Uri? _currentUri;

        public CoreWebView2? Core { get; private set; }

        public WebViewRuntime(IAppPaths appPaths, IOptions<WebViewOptions> webViewOptions)
        {
            _appPaths = appPaths;
            _webViewOptions = webViewOptions.Value;
        }

        public event Action<WebMessageEnvelope>? WebMessageJsonReceived;
        public event Action? NavigationCompleted;
        private static long _lastRejectedLogTicks;
        private static readonly long RejectedLogIntervalTicks = TimeSpan.FromSeconds(10).Ticks;
        private static long _lastBridgeDisarmedLogTicks;
        private static long _lastBridgeIgnoredLogTicks;
        private static long _lastNavigationBlockedLogTicks;
        private static readonly long BridgeLogIntervalTicks = TimeSpan.FromSeconds(10).Ticks;

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
                Core.NavigationStarting += (_, e) => HandleNavigationStarting(e);
                Core.SourceChanged += (_, __) => UpdateBridgeState(Core.Source);
                Core.WebMessageReceived += (_, e) =>
                {
                    if (!_bridgeArmed)
                    {
                        LogBridgeIgnoredMessage();
                        return;
                    }

                    var source = e.Source;
                    if (!WebMessageOriginGuard.TryIsAllowed(source, out var sourceUri))
                    {
                        LogRejectedWebMessage(source);
                        return;
                    }

                    var json = e.WebMessageAsJson;
                    if (string.IsNullOrWhiteSpace(json))
                        return;

                    WebMessageJsonReceived?.Invoke(new WebMessageEnvelope(json, sourceUri!));
                };
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

        private void HandleNavigationStarting(CoreWebView2NavigationStartingEventArgs e)
        {
            var uri = e.Uri;
            if (!WebOriginPolicy.TryIsNavigationAllowed(uri, out _))
            {
                e.Cancel = true;
                LogBlockedNavigation(uri);
            }
        }

        private void UpdateBridgeState(string? source)
        {
            Uri? parsed = null;
            if (Uri.TryCreate(source, UriKind.Absolute, out var candidate))
                parsed = candidate;

            _currentUri = parsed;

            var wasArmed = _bridgeArmed;
            _bridgeArmed = WebOriginPolicy.TryIsBridgeAllowed(source, out _);

            if (wasArmed && !_bridgeArmed)
                LogBridgeDisarmed(source);
        }

        private static void LogRejectedWebMessage(string? source)
        {
            var nowTicks = DateTimeOffset.UtcNow.Ticks;
            var lastTicks = Interlocked.Read(ref _lastRejectedLogTicks);
            if (nowTicks - lastTicks < RejectedLogIntervalTicks)
                return;

            if (Interlocked.CompareExchange(ref _lastRejectedLogTicks, nowTicks, lastTicks) != lastTicks)
                return;

            ValLog.Warn(nameof(WebViewRuntime),
                $"Blocked web message from non-allowlisted origin: {source ?? "<null>"}");
        }

        private void LogBridgeDisarmed(string? source)
        {
            if (!ShouldLog(ref _lastBridgeDisarmedLogTicks, BridgeLogIntervalTicks))
                return;

            ValLog.Warn(nameof(WebViewRuntime),
                $"Bridge disarmed due to untrusted origin: {source ?? "<null>"}");
        }

        private void LogBridgeIgnoredMessage()
        {
            if (!ShouldLog(ref _lastBridgeIgnoredLogTicks, BridgeLogIntervalTicks))
                return;

            var origin = _currentUri?.ToString() ?? "<null>";
            ValLog.Warn(nameof(WebViewRuntime),
                $"Ignoring web message while bridge disarmed (current origin: {origin})");
        }

        private static void LogBlockedNavigation(string? uri)
        {
            if (!ShouldLog(ref _lastNavigationBlockedLogTicks, BridgeLogIntervalTicks))
                return;

            ValLog.Warn(nameof(WebViewRuntime),
                $"Canceled navigation to unsafe or unknown URI: {uri ?? "<null>"}");
        }

        private static bool ShouldLog(ref long lastTicksRef, long intervalTicks)
        {
            var nowTicks = DateTimeOffset.UtcNow.Ticks;
            var lastTicks = Interlocked.Read(ref lastTicksRef);
            if (nowTicks - lastTicks < intervalTicks)
                return false;

            return Interlocked.CompareExchange(ref lastTicksRef, nowTicks, lastTicks) == lastTicks;
        }
    }
}
