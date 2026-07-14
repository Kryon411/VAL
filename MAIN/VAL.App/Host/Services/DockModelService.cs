using System;
using System.Linq;
using System.Text.Json;

using VAL.Contracts;
using VAL.Host.WebMessaging;

namespace VAL.App.Host.Services
{
    public sealed class DockModelService : IDockModelService
    {
        private readonly IWebMessageSender _webMessageSender;
        private readonly IPrivacySettingsService _privacySettingsService;
        private readonly ISessionContext _sessionContext;
        private readonly IModuleLoader _moduleLoader;
        private readonly ILog _log;
        private readonly object _sync = new();

        private bool _continuumLoggingEnabled = true;
        private bool _portalCaptureEnabled = true;
        private bool _portalEnabled;
        private bool _portalPrivacyAllowed = true;
        private int _portalCount;

        public DockModelService(
            IWebMessageSender webMessageSender,
            IPrivacySettingsService privacySettingsService,
            ISessionContext sessionContext,
            IModuleLoader moduleLoader,
            ILog log)
        {
            _webMessageSender = webMessageSender;
            _privacySettingsService = privacySettingsService;
            _sessionContext = sessionContext;
            _moduleLoader = moduleLoader;
            _log = log ?? throw new ArgumentNullException(nameof(log));

            ApplyPrivacySnapshot(_privacySettingsService.GetSnapshot());
            _privacySettingsService.SettingsChanged += OnPrivacySettingsChanged;
        }

        public void Publish(string? chatId = null)
        {
            var resolvedChatId = string.IsNullOrWhiteSpace(chatId)
                ? _sessionContext.ActiveChatId
                : chatId;
            var snapshot = CreateSnapshot(resolvedChatId);
            var model = DockModelBuilder.Build(snapshot);

            try
            {
                var payload = JsonSerializer.SerializeToElement(model);
                _webMessageSender.Send(new MessageEnvelope
                {
                    Type = WebMessageTypes.Event,
                    Name = WebCommandNames.DockModel,
                    ChatId = resolvedChatId,
                    Source = "host",
                    Payload = payload,
                    Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                });
            }
            catch
            {
                _log.Warn(nameof(DockModelService), "Failed to publish dock model.");
            }
        }

        public void UpdatePortalState(bool enabled, bool privacyAllowed, int count)
        {
            lock (_sync)
            {
                _portalEnabled = enabled;
                _portalPrivacyAllowed = privacyAllowed;
                _portalCount = Math.Clamp(count, 0, 10);
            }

            Publish();
        }

        private void OnPrivacySettingsChanged(PrivacySettingsSnapshot snapshot)
        {
            ApplyPrivacySnapshot(snapshot);
            Publish();
        }

        private void ApplyPrivacySnapshot(PrivacySettingsSnapshot snapshot)
        {
            lock (_sync)
            {
                _continuumLoggingEnabled = snapshot.ContinuumLoggingEnabled;
                _portalCaptureEnabled = snapshot.PortalCaptureEnabled;
                _portalPrivacyAllowed = snapshot.PortalCaptureEnabled;
            }
        }

        private DockModelSnapshot CreateSnapshot(string? chatId)
        {
            bool continuumLoggingEnabled;
            bool portalCaptureEnabled;
            bool portalEnabled;
            bool portalPrivacyAllowed;
            int portalCount;

            lock (_sync)
            {
                continuumLoggingEnabled = _continuumLoggingEnabled;
                portalCaptureEnabled = _portalCaptureEnabled;
                portalEnabled = _portalEnabled;
                portalPrivacyAllowed = _portalPrivacyAllowed;
                portalCount = _portalCount;
            }

            return new DockModelSnapshot
            {
                ChatId = chatId,
                ContinuumLoggingEnabled = continuumLoggingEnabled,
                PortalCaptureEnabled = portalCaptureEnabled,
                PortalEnabled = portalEnabled,
                PortalPrivacyAllowed = portalPrivacyAllowed,
                PortalCount = portalCount,
                ModuleStatuses = _moduleLoader.GetModuleStatuses().ToList()
            };
        }
    }
}
