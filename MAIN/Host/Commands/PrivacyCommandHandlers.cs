using System;
using VAL.Contracts;
using VAL.Host.Logging;
using VAL.Host.Services;
using VAL.Host.WebMessaging;

namespace VAL.Host.Commands
{
    internal sealed class PrivacyCommandHandlers
        : ICommandRegistryContributor
    {
        private static readonly string[] RequiredEnabled = { "enabled" };
        private readonly IPrivacySettingsService _privacySettingsService;
        private readonly IAppPaths _appPaths;
        private readonly IProcessLauncher _processLauncher;
        private readonly IDataWipeService _dataWipeService;
        private readonly IToastHub _toastHub;
        private readonly IWebMessageSender _webMessageSender;
        private readonly ILog _log;
        private readonly RateLimiter _rateLimiter = new();
        private static readonly TimeSpan LogInterval = TimeSpan.FromSeconds(10);

        public PrivacyCommandHandlers(
            IPrivacySettingsService privacySettingsService,
            IAppPaths appPaths,
            IProcessLauncher processLauncher,
            IDataWipeService dataWipeService,
            IToastHub toastHub,
            IWebMessageSender webMessageSender,
            ILog log)
        {
            _privacySettingsService = privacySettingsService ?? throw new ArgumentNullException(nameof(privacySettingsService));
            _appPaths = appPaths ?? throw new ArgumentNullException(nameof(appPaths));
            _processLauncher = processLauncher ?? throw new ArgumentNullException(nameof(processLauncher));
            _dataWipeService = dataWipeService ?? throw new ArgumentNullException(nameof(dataWipeService));
            _toastHub = toastHub ?? throw new ArgumentNullException(nameof(toastHub));
            _webMessageSender = webMessageSender ?? throw new ArgumentNullException(nameof(webMessageSender));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public void Register(CommandRegistry registry)
        {
            ArgumentNullException.ThrowIfNull(registry);

            registry.Register(new CommandSpec(
                WebCommandNames.PrivacyCommandSetContinuumLogging,
                "Privacy",
                RequiredEnabled,
                HandleSetContinuumLogging));
            registry.Register(new CommandSpec(
                WebCommandNames.PrivacyCommandSetPortalCapture,
                "Privacy",
                RequiredEnabled,
                HandleSetPortalCapture));
            registry.Register(new CommandSpec(
                WebCommandNames.PrivacyCommandOpenDataFolder,
                "Privacy",
                Array.Empty<string>(),
                HandleOpenDataFolder));
            registry.Register(new CommandSpec(
                WebCommandNames.PrivacyCommandWipeData,
                "Privacy",
                Array.Empty<string>(),
                HandleWipeData));
        }

        public void HandleSetContinuumLogging(HostCommand cmd)
        {
            try
            {
                if (!cmd.TryGetBool("enabled", out var enabled))
                    return;

                var updated = _privacySettingsService.UpdateContinuumLogging(enabled);
                if (updated)
                    SendSettingsSync(_privacySettingsService.GetSnapshot());
            }
            catch (Exception ex)
            {
                LogCommandFailure("continuum_logging", cmd, ex);
            }
        }

        public void HandleSetPortalCapture(HostCommand cmd)
        {
            try
            {
                if (!cmd.TryGetBool("enabled", out var enabled))
                    return;

                var updated = _privacySettingsService.UpdatePortalCapture(enabled);
                if (updated)
                    SendSettingsSync(_privacySettingsService.GetSnapshot());
            }
            catch (Exception ex)
            {
                LogCommandFailure("portal_capture", cmd, ex);
            }
        }

        public void HandleOpenDataFolder(HostCommand cmd)
        {
            try
            {
                _processLauncher.OpenFolder(_appPaths.DataRoot);
            }
            catch (Exception ex)
            {
                LogCommandFailure("open_data_folder", cmd, ex);
            }
        }

        public void HandleWipeData(HostCommand cmd)
        {
            try
            {
                var result = _dataWipeService.WipeData();
                var reason = ToastReasonParser.Parse(cmd.TryGetString("reason", out var rawReason) ? rawReason : null, ToastReason.DockClick);

                if (result.Success)
                {
                    _toastHub.TryShow(
                        ToastKey.DataWipeCompleted,
                        bypassLaunchQuiet: true,
                        origin: ToastOrigin.HostCommand,
                        reason: reason);
                }
                else
                {
                    _toastHub.TryShow(
                        ToastKey.DataWipePartial,
                        bypassLaunchQuiet: true,
                        origin: ToastOrigin.HostCommand,
                        reason: reason);
                }
            }
            catch (Exception ex)
            {
                LogCommandFailure("wipe_data", cmd, ex);
                _toastHub.TryShow(
                    ToastKey.DataWipePartial,
                    bypassLaunchQuiet: true,
                    origin: ToastOrigin.HostCommand,
                    reason: ToastReason.Background);
            }
        }

        private void SendSettingsSync(PrivacySettingsSnapshot snapshot)
        {
            try
            {
                var payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                {
                    version = snapshot.Version,
                    continuumLoggingEnabled = snapshot.ContinuumLoggingEnabled,
                    portalCaptureEnabled = snapshot.PortalCaptureEnabled
                });

                _webMessageSender.Send(new MessageEnvelope
                {
                    Type = WebMessageTypes.Event,
                    Name = "privacy.settings.sync",
                    Source = "host",
                    Payload = payload,
                    Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                });
            }
            catch
            {
                _log.Warn(nameof(PrivacyCommandHandlers), "Failed to send privacy settings sync.");
            }
        }

        private void LogCommandFailure(string action, HostCommand cmd, Exception ex)
        {
            var key = $"cmd.fail.privacy.{action}";
            if (!_rateLimiter.Allow(key, LogInterval))
                return;

            var sourceHost = cmd.SourceUri?.Host ?? "unknown";
            _log.Warn(nameof(PrivacyCommandHandlers),
                $"Privacy command failed ({action}) for {cmd.Type} (source: {sourceHost}). {LogSanitizer.Sanitize(ex.Message)}");
        }
    }
}
