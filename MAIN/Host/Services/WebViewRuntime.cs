using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using VAL.Host;
using Microsoft.Extensions.Options;
using VAL.Host.Options;
using VAL.Host.Security;
using VAL.Host.WebMessaging;

namespace VAL.Host.Services
{
    public sealed class WebViewRuntime : IWebViewRuntime, IDisposable
    {
        private readonly IAppPaths _appPaths;
        private readonly WebViewOptions _webViewOptions;
        private readonly IWebViewSessionNonce _sessionNonce;
        private readonly SemaphoreSlim _initLock = new(1, 1);
        private Task? _initTask;
        private bool _eventsWired;
        private volatile bool _bridgeArmed;
        private Uri? _currentUri;
        private Dispatcher? _dispatcher;
        private EventHandler<CoreWebView2NavigationCompletedEventArgs>? _navigationCompletedHandler;
        private EventHandler<CoreWebView2NavigationStartingEventArgs>? _navigationStartingHandler;
        private EventHandler<CoreWebView2SourceChangedEventArgs>? _sourceChangedHandler;
        private EventHandler<CoreWebView2WebMessageReceivedEventArgs>? _webMessageReceivedHandler;
        private EventHandler<CoreWebView2NewWindowRequestedEventArgs>? _newWindowRequestedHandler;

        public CoreWebView2? Core { get; private set; }

        public WebViewRuntime(IAppPaths appPaths, IOptions<WebViewOptions> webViewOptions, IWebViewSessionNonce sessionNonce)
        {
            _appPaths = appPaths;
            _webViewOptions = webViewOptions.Value;
            _sessionNonce = sessionNonce;
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
            ArgumentNullException.ThrowIfNull(control);

            _dispatcher ??= control.Dispatcher;

            await _initLock.WaitAsync();
            try
            {
                if (_initTask != null)
                {
                    await _initTask;
                    return;
                }

                _initTask = RunOnUiAsync(() => InitializeCoreAsync(control));
            }
            finally
            {
                _initLock.Release();
            }

            try
            {
                await _initTask;
            }
            catch (Exception ex)
            {
                ValLog.Error(nameof(WebViewRuntime), $"WebView2 initialization failed: {ex}");
                throw;
            }
        }

        public void PostJson(string json)
        {
            RunOnUiThread(() =>
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
            });
        }

        public async Task ExecuteScriptAsync(string js)
        {
            await RunOnUiAsync(async () =>
            {
                var core = Core;
                if (core == null)
                {
                    ValLog.Warn(nameof(WebViewRuntime), "ExecuteScriptAsync called before WebView2 initialization.");
                    return;
                }

                try
                {
                    await core.ExecuteScriptAsync(js);
                }
                catch
                {
                    ValLog.Warn(nameof(WebViewRuntime), "Failed to execute script in WebView2.");
                }
            });
        }

        private async Task InitializeCoreAsync(WebView2 control)
        {
            // Profile root (isolated WebView2 user data)
            var userData = _appPaths.ProfileRoot;
            Directory.CreateDirectory(userData);

            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userData);
            await RunOnUiAsync(async () =>
            {
                await control.EnsureCoreWebView2Async(env);

                Core = control.CoreWebView2;
                if (Core == null)
                {
                    var message = "WebView2 core initialization failed. Runtime missing or initialization did not complete.";
                    ValLog.Error(nameof(WebViewRuntime), message);
                    throw new InvalidOperationException(message);
                }

                Core.Settings.AreDevToolsEnabled = _webViewOptions.EffectiveAllowDevTools;
                Core.Settings.IsStatusBarEnabled = false;
                Core.Settings.IsWebMessageEnabled = true;

                await InitializeSessionNonceScriptAsync();

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
                    _navigationCompletedHandler ??= (_, __) => NavigationCompleted?.Invoke();
                    _navigationStartingHandler ??= (_, e) => HandleNavigationStarting(e);
                    _sourceChangedHandler ??= (_, __) => UpdateBridgeState(Core?.Source);
                    _webMessageReceivedHandler ??= (_, e) =>
                    {
                        if (!_bridgeArmed)
                        {
                            LogBridgeIgnoredMessage();
                            return;
                        }

                        var source = e.Source;
                        var json = e.WebMessageAsJson;
                        if (string.IsNullOrWhiteSpace(json))
                            return;

                        if (!MessageEnvelope.TryParse(json, out var envelope))
                        {
                            LogRejectedWebMessage(source, "invalid_payload");
                            return;
                        }

                        if (!WebMessageOriginGuard.TryIsAllowed(source, envelope.Nonce, _sessionNonce.Value, out var sourceUri, out var reason))
                        {
                            LogRejectedWebMessage(source, reason ?? "nonce_or_origin_rejected");
                            return;
                        }

                        WebMessageJsonReceived?.Invoke(new WebMessageEnvelope(json, sourceUri!));
                    };
                    _newWindowRequestedHandler ??= (_, e) => HandleNewWindowRequested(e);

                    Core.NavigationCompleted -= _navigationCompletedHandler;
                    Core.NavigationStarting -= _navigationStartingHandler;
                    Core.SourceChanged -= _sourceChangedHandler;
                    Core.WebMessageReceived -= _webMessageReceivedHandler;
                    Core.NewWindowRequested -= _newWindowRequestedHandler;

                    Core.NavigationCompleted += _navigationCompletedHandler;
                    Core.NavigationStarting += _navigationStartingHandler;
                    Core.SourceChanged += _sourceChangedHandler;
                    Core.WebMessageReceived += _webMessageReceivedHandler;
                    Core.NewWindowRequested += _newWindowRequestedHandler;
                    _eventsWired = true;
                }
            });
        }

        private void HandleNewWindowRequested(CoreWebView2NewWindowRequestedEventArgs e)
        {
            if (!_webViewOptions.BlockNewWindow)
                return;

            if (_dispatcher != null && !_dispatcher.CheckAccess())
            {
                _dispatcher.Invoke(() => HandleNewWindowRequested(e));
                return;
            }

            var core = Core;
            try
            {
                if (core == null)
                {
                    e.Handled = true;
                    return;
                }

                e.NewWindow = core;
                e.Handled = true;
            }
            catch
            {
                try
                {
                    e.Handled = true;

                    var uri = e.Uri;
                    if (!string.IsNullOrWhiteSpace(uri))
                        core?.Navigate(uri);
                }
                catch
                {
                    // Never let window routing break the host.
                }
            }
        }

        private static void HandleNavigationStarting(CoreWebView2NavigationStartingEventArgs e)
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

        private static void LogRejectedWebMessage(string? source, string reason)
        {
            var nowTicks = DateTimeOffset.UtcNow.Ticks;
            var lastTicks = Interlocked.Read(ref _lastRejectedLogTicks);
            if (nowTicks - lastTicks < RejectedLogIntervalTicks)
                return;

            if (Interlocked.CompareExchange(ref _lastRejectedLogTicks, nowTicks, lastTicks) != lastTicks)
                return;

            ValLog.Warn(nameof(WebViewRuntime),
                $"Rejected web message ({reason}): {source ?? "<null>"}");
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

        private async Task InitializeSessionNonceScriptAsync()
        {
            var nonce = _sessionNonce.Value;
            if (string.IsNullOrWhiteSpace(nonce))
                return;

            var nonceJson = JsonSerializer.Serialize(nonce);
            var script = $"window.__VAL_NONCE = {nonceJson};";

            await RunOnUiAsync(async () =>
            {
                var core = Core;
                if (core == null)
                {
                    ValLog.Warn(nameof(WebViewRuntime), "Failed to inject WebView session nonce.");
                    return;
                }

                try
                {
                    await core.AddScriptToExecuteOnDocumentCreatedAsync(script);
                }
                catch
                {
                    ValLog.Warn(nameof(WebViewRuntime), "Failed to inject WebView session nonce.");
                }
            });
        }

        private Task RunOnUiAsync(Func<Task> action)
        {
            if (_dispatcher == null || _dispatcher.CheckAccess())
                return action();

            return _dispatcher.InvokeAsync(action).Task.Unwrap();
        }

        private void RunOnUiThread(Action action)
        {
            if (_dispatcher == null || _dispatcher.CheckAccess())
            {
                action();
                return;
            }

            _dispatcher.Invoke(action);
        }

        public void Dispose()
        {
            _initLock.Dispose();
        }
    }
}
