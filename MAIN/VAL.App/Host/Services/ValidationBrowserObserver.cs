using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

using VAL.Host;
using VAL.Host.Security;

namespace VAL.App.Host.Services;

// Interactive validation only: never collect page text, URLs, cookies or composer values.
internal sealed class ValidationBrowserObserver : IDisposable
{
    internal const string SnapshotScript = """
        (() => {
          if (window !== window.top || !['https://chatgpt.com','https://chat.openai.com'].includes(location.origin)) return null;
          const count = selector => document.querySelectorAll(selector).length;
          const ownedControl = document.querySelectorAll('button[data-val-ui="1"]')[0];
          const controlStyle = ownedControl ? getComputedStyle(ownedControl) : null;
          const scrollAncestors = [];
          const firstUnit = Array.from(document.querySelectorAll('[data-chatgpt-search-unit-key]')).find(element =>
            element.isConnected && !element.closest('[hidden], [inert], [aria-hidden="true"]') &&
            element.getBoundingClientRect().height > 0);
          for (let element = firstUnit?.parentElement; element && scrollAncestors.length < 20; element = element.parentElement) {
            const style = getComputedStyle(element);
            scrollAncestors.push({
              tag: element.tagName, overflowY: style.overflowY, display: style.display,
              flexDirection: style.flexDirection, clientHeight: element.clientHeight,
              scrollHeight: element.scrollHeight, scrollTop: element.scrollTop
            });
          }
          return {
            ready: document.readyState === 'complete',
            bridge: typeof window.chrome?.webview?.postMessage === 'function',
            clientInitialized: window.__VAL_CONTINUUM_HEALTH?.initialized === true,
            clientFailed: window.__VAL_CONTINUUM_HEALTH?.failed === true,
            pendingInjection: window.__VAL_CONTINUUM_HEALTH?.pendingInjection === true,
            awaitingNewChat: window.__VAL_CONTINUUM_HEALTH?.awaitingNewChat === true,
            awaitingSignal: window.__VAL_CONTINUUM_HEALTH?.awaitingSignal === true,
            newChatRoot: location.pathname === '/',
            valControls: {
              dockStylesheets: count('style[id^="val-style-dock-"]'),
              markedControls: count('button[data-val-ui="1"]'),
              rim: controlStyle?.getPropertyValue('--val-rim').trim() || null,
              faceTop: controlStyle?.getPropertyValue('--val-face-top').trim() || null
            },
            turns: count("[data-testid^='conversation-turn-']"),
            messages: count('[data-message-id]'),
            userRoles: count('[data-message-author-role="user"]'),
            assistantRoles: count('[data-message-author-role="assistant"]'),
            copyActions: count('button[data-testid="copy-turn-action-button"],button[aria-label="Copy"]'),
            promptFields: count('#prompt-textarea,textarea#mobile-composer-prompt'),
            stopButtons: count('button[data-testid="stop-button"]'),
            distinctUnitKeys: new Set(Array.from(document.querySelectorAll('[data-chatgpt-search-unit-key]')).map(element => element.getAttribute('data-chatgpt-search-unit-key'))).size,
            renderedUnits: Array.from(document.querySelectorAll('[data-chatgpt-search-unit-key]')).filter(element => {
              const rect = element.getBoundingClientRect();
              const style = getComputedStyle(element);
              return element.isConnected && !element.closest('[hidden], [inert], [aria-hidden="true"]') &&
                rect.width > 0 && rect.height > 0 && style.display !== 'none' && style.visibility !== 'hidden';
            }).length,
            scrollAncestors,
            captureUnits: Array.from(document.querySelectorAll('[data-chatgpt-search-unit-key]')).slice(0, 40).map(element => ({
              hasKey: !!element.getAttribute('data-chatgpt-search-unit-key'),
              user: !!element.querySelector('[data-user-message-bubble]'),
              assistant: !!element.querySelector('[data-conversation-role="assistant"]'),
              markdown: !!element.querySelector('[data-markdown-text-style]'),
              selectionMarkdown: !!element.querySelector('[data-chatgpt-selection-message-id] [data-markdown-text-style]'),
              actions: !!element.querySelector('button[aria-label="Copy"]'),
              parentActions: !!element.parentElement?.querySelector('button[aria-label="Copy"]'),
              grandparentActions: !!element.parentElement?.parentElement?.querySelector('button[aria-label="Copy"]')
            })),
            structure: Array.from(document.querySelectorAll('main *')).filter(element =>
              element.tagName === 'ARTICLE' || element.hasAttribute('contenteditable') ||
              element.hasAttribute('data-testid') || element.hasAttribute('role') ||
              Array.from(element.attributes).some(attribute => attribute.name.startsWith('data-'))
            ).slice(0, 80).map(element => ({
              tag: element.tagName,
              role: element.getAttribute('role'),
              testId: element.getAttribute('data-testid'),
              attributes: Array.from(element.attributes).map(attribute => attribute.name)
            }))
          };
        })()
        """;

    private readonly IAppPaths _paths;
    private readonly IWebViewRuntime _runtime;
    private readonly ILog _log;
    private DispatcherTimer? _timer;
    private bool _busy;
    private bool _disposed;

    public ValidationBrowserObserver(IAppPaths paths, IWebViewRuntime runtime, ILog log)
    {
        _paths = paths;
        _runtime = runtime;
        _log = log;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_timer != null) return;
        _timer = new DispatcherTimer(DispatcherPriority.Background, Application.Current.Dispatcher)
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _timer.Tick += Capture;
        _timer.Start();
    }

    private async void Capture(object? sender, EventArgs e)
    {
        if (_disposed || _busy) return;
        _busy = true;
        try
        {
            BrowserMetrics? browser = null;
            var core = _runtime.Core;
            if (_runtime.IsBridgeReady && core != null && WebOriginPolicy.TryIsBridgeAllowed(core.Source, out _))
            {
                var json = await core.ExecuteScriptAsync(SnapshotScript);
                browser = JsonSerializer.Deserialize<BrowserMetrics>(json);
            }
            if (!_disposed)
                JsonStateFile.Write(Path.Combine(_paths.StateRoot, "validation.browser.json"),
                    new { CapturedUtc = DateTimeOffset.UtcNow, BridgeReady = _runtime.IsBridgeReady, Browser = browser });
        }
        catch
        {
            if (!_disposed) _log.Warn(nameof(ValidationBrowserObserver), "Content-free browser snapshot failed.");
        }
        finally { _busy = false; }
    }

    public void Dispose()
    {
        _disposed = true;
        if (_timer == null) return;
        _timer.Stop();
        _timer.Tick -= Capture;
        _timer = null;
    }

    private sealed class BrowserMetrics
    {
        public bool ready { get; set; }
        public bool bridge { get; set; }
        public bool clientInitialized { get; set; }
        public bool clientFailed { get; set; }
        public bool pendingInjection { get; set; }
        public bool awaitingNewChat { get; set; }
        public bool awaitingSignal { get; set; }
        public bool newChatRoot { get; set; }
        public ControlMetrics? valControls { get; set; }
        public int turns { get; set; }
        public int messages { get; set; }
        public int userRoles { get; set; }
        public int assistantRoles { get; set; }
        public int copyActions { get; set; }
        public int promptFields { get; set; }
        public int stopButtons { get; set; }
        public int distinctUnitKeys { get; set; }
        public int renderedUnits { get; set; }
        public ScrollNode[]? scrollAncestors { get; set; }
        public CaptureUnit[]? captureUnits { get; set; }
        public StructureNode[]? structure { get; set; }
    }

    private sealed class ControlMetrics
    {
        public int dockStylesheets { get; set; }
        public int markedControls { get; set; }
        public string? rim { get; set; }
        public string? faceTop { get; set; }
    }

    private sealed class ScrollNode
    {
        public string? tag { get; set; }
        public string? overflowY { get; set; }
        public string? display { get; set; }
        public string? flexDirection { get; set; }
        public double clientHeight { get; set; }
        public double scrollHeight { get; set; }
        public double scrollTop { get; set; }
    }

    private sealed class CaptureUnit
    {
        public bool hasKey { get; set; }
        public bool user { get; set; }
        public bool assistant { get; set; }
        public bool markdown { get; set; }
        public bool selectionMarkdown { get; set; }
        public bool actions { get; set; }
        public bool parentActions { get; set; }
        public bool grandparentActions { get; set; }
    }

    private sealed class StructureNode
    {
        public string? tag { get; set; }
        public string? role { get; set; }
        public string? testId { get; set; }
        public string[]? attributes { get; set; }
    }
}
