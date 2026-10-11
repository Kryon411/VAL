using System;
using System.Threading.Tasks;

using VAL.Host.Portal;

namespace VAL.Host.Services
{
    public sealed class PortalRuntimeService : IPortalRuntimeService, IDisposable
    {
        internal const string FocusScript = """
            (() => {
              try {
                const selectors = ['form[data-chatgpt-composer] [data-composer-markdown][contenteditable="true"][role="textbox"]',
                  '#prompt-textarea[contenteditable="true"]',
                  '#mobile-composer-prompt[contenteditable="true"]',
                  'textarea#prompt-textarea', 'textarea#mobile-composer-prompt',
                  'textarea[data-testid="prompt-textarea"]'];
                for (const selector of selectors) {
                  for (const element of document.querySelectorAll(selector)) {
                    if (!element.isConnected || element.disabled || element.readOnly ||
                      element.getAttribute('aria-disabled') === 'true') continue;
                    const rect = element.getBoundingClientRect();
                    const style = getComputedStyle(element);
                    if (rect.width <= 0 || rect.height <= 0 || rect.bottom <= 0 ||
                      rect.top >= window.innerHeight || style.display === 'none' ||
                      style.visibility === 'hidden') continue;
                    element.focus();
                    if (document.activeElement === element || element.contains(document.activeElement)) return true;
                  }
                }
              } catch {}
              return false;
            })()
            """;

        private readonly PortalRuntime _portalRuntime;
        private readonly IWebViewRuntime _webViewRuntime;
        private readonly IPrivacySettingsService _privacySettingsService;
        private readonly IPortalRuntimeStateManager _portalRuntimeStateManager;
        private readonly ILog _log;
        private bool _initialized;

        public PortalRuntimeService(
            PortalRuntime portalRuntime,
            IWebViewRuntime webViewRuntime,
            IPrivacySettingsService privacySettingsService,
            IPortalRuntimeStateManager portalRuntimeStateManager,
            ILog log)
        {
            _portalRuntime = portalRuntime;
            _webViewRuntime = webViewRuntime;
            _privacySettingsService = privacySettingsService;
            _portalRuntimeStateManager = portalRuntimeStateManager;
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _privacySettingsService.SettingsChanged += OnPrivacySettingsChanged;
        }

        public void Initialize(Action focusControl)
        {
            if (_initialized)
                return;

            try
            {
                _portalRuntime.Initialize(
                    async () =>
                    {
                        try
                        {
                            focusControl?.Invoke();
                        }
                        catch
                        {
                            _log.Warn(nameof(PortalRuntimeService), "Focus callback failed.");
                            return false;
                        }

                        return await FocusComposerAsync();
                    }
                );

                ApplyPrivacySettings(_privacySettingsService.GetSnapshot());
                _initialized = true;
            }
            catch
            {
                _log.Warn(nameof(PortalRuntimeService), "Portal runtime initialization failed.");
            }
        }

        public void AttachWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
                return;

            try
            {
                _portalRuntime.AttachWindow(hwnd);
            }
            catch
            {
                _log.Warn(nameof(PortalRuntimeService), "Failed to attach portal window.");
            }
        }

        private void OnPrivacySettingsChanged(PrivacySettingsSnapshot snapshot)
        {
            ApplyPrivacySettings(snapshot);
        }

        private async Task<bool> FocusComposerAsync()
        {
            try
            {
                var result = await _webViewRuntime.EvaluateScriptAsync(FocusScript)
                    .WaitAsync(TimeSpan.FromSeconds(3));
                return string.Equals(result, "true", StringComparison.Ordinal);
            }
            catch { _log.Warn(nameof(PortalRuntimeService), "Composer focus was unavailable."); }
            return false;
        }

        public void Dispose()
        {
            _privacySettingsService.SettingsChanged -= OnPrivacySettingsChanged;
        }

        private void ApplyPrivacySettings(PrivacySettingsSnapshot snapshot)
        {
            try
            {
                _portalRuntimeStateManager.SetPrivacyAllowed(snapshot.PortalCaptureEnabled);
            }
            catch
            {
                _log.Warn(nameof(PortalRuntimeService), "Failed to apply portal privacy settings.");
            }
        }
    }
}
