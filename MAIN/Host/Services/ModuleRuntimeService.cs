using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using VAL.Continuum;
using VAL.Host.Abyss;
using VAL.Host.WebMessaging;
using VAL.Host.Startup;

namespace VAL.Host.Services
{
    public sealed class ModuleRuntimeService : IModuleRuntimeService
    {
        private readonly IModuleLoader _moduleLoader;
        private readonly IWebViewRuntime _webViewRuntime;
        private readonly IWebMessageSender _webMessageSender;
        private readonly IPrivacySettingsService _privacySettingsService;
        private readonly StartupOptions _startupOptions;

        private CoreWebView2? _modulesInitializedForCore;
        private int _modulesInitInFlight;
        private bool _started;

        public ModuleRuntimeService(
            IModuleLoader moduleLoader,
            IWebViewRuntime webViewRuntime,
            IWebMessageSender webMessageSender,
            IPrivacySettingsService privacySettingsService,
            StartupOptions startupOptions)
        {
            _moduleLoader = moduleLoader;
            _webViewRuntime = webViewRuntime;
            _webMessageSender = webMessageSender;
            _privacySettingsService = privacySettingsService;
            _startupOptions = startupOptions;
        }

        public void Start()
        {
            if (_started)
                return;

            if (_startupOptions.SafeMode)
            {
                _started = true;
                return;
            }

            _webViewRuntime.NavigationCompleted += async () =>
            {
                try
                {
                    await EnsureModulesInitializedAsync();
                }
                catch
                {
                    ValLog.Warn(nameof(ModuleRuntimeService), "Module initialization failed after navigation.");
                }
            };

            _started = true;

            _ = EnsureModulesInitializedAsync();
        }

        public async Task EnsureModulesInitializedAsync()
        {
            if (_startupOptions.SafeMode)
                return;

            var core = _webViewRuntime.Core;
            if (core == null)
                return;

            if (ReferenceEquals(_modulesInitializedForCore, core))
                return;

            if (Interlocked.CompareExchange(ref _modulesInitInFlight, 1, 0) != 0)
                return;

            try
            {
                await _moduleLoader.InitializeAsync(core);
                _modulesInitializedForCore = core;

                ContinuumHost.PostToWebMessage = _webMessageSender.Send;
                AbyssRuntime.Initialize(_webMessageSender);
                SendPrivacySettings();
            }
            catch (System.Exception ex)
            {
                _modulesInitializedForCore = null;
                ValLog.Warn(nameof(ModuleRuntimeService), $"Module initialization failed. {ex.GetType().Name}: {ex.Message}");
                ValLog.Error(nameof(ModuleRuntimeService), ex.ToString());
            }
            finally
            {
                Interlocked.Exchange(ref _modulesInitInFlight, 0);
            }
        }

        private void SendPrivacySettings()
        {
            try
            {
                var snapshot = _privacySettingsService.GetSnapshot();
                var payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                {
                    version = snapshot.Version,
                    continuumLoggingEnabled = snapshot.ContinuumLoggingEnabled,
                    portalCaptureEnabled = snapshot.PortalCaptureEnabled
                });

                _webMessageSender.Send(new WebMessaging.MessageEnvelope
                {
                    Type = "event",
                    Name = "privacy.settings.sync",
                    Source = "host",
                    Payload = payload,
                    Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                });
            }
            catch
            {
                ValLog.Warn(nameof(ModuleRuntimeService), "Failed to send privacy settings sync.");
            }
        }
    }
}
