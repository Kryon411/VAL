using System;
using VAL.Host.Portal;

namespace VAL.Host.Services
{
    public sealed class PortalRuntimeService : IPortalRuntimeService
    {
        private const string FocusScript = "(()=>{try{const selectors=['form textarea','textarea[placeholder]','div[contenteditable=\\\"true\\\"][role=\\\"textbox\\\"]','div.ProseMirror[contenteditable=\\\"true\\\"]','div[contenteditable=\\\"true\\\"][data-slate-editor=\\\"true\\\"]'];for(const s of selectors){  const el=document.querySelector(s);  if(el){ try{ el.focus(); }catch{}; try{ el.click(); }catch{}; return true; }}// fallback: find any visible contenteditable in the bottom composer regionconst cands=[...document.querySelectorAll('div[contenteditable=\\\"true\\\"]')].filter(e=>{  const r=e.getBoundingClientRect();  return r.width>100 && r.height>20 && r.bottom> (window.innerHeight*0.55);});if(cands.length){  const el=cands[cands.length-1];  try{ el.focus(); }catch{}; try{ el.click(); }catch{}; return true;}}catch(e){} return false;})()";

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
                    () =>
                    {
                        try
                        {
                            focusControl?.Invoke();
                        }
                        catch
                        {
                            _log.Warn(nameof(PortalRuntimeService), "Focus callback failed.");
                        }

                        _ = _webViewRuntime.ExecuteScriptAsync(FocusScript);
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
