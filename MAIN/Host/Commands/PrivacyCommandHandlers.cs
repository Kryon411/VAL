using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using VAL.Contracts;
using VAL.Host.Logging;
using VAL.Host.Services;
using VAL.Host.WebMessaging;

namespace VAL.Host.Commands
{
    internal static class PrivacyCommandHandlers
    {
        private static readonly RateLimiter RateLimiter = new();
        private static readonly TimeSpan LogInterval = TimeSpan.FromSeconds(10);

        public static void HandleSetContinuumLogging(HostCommand cmd)
        {
            try
            {
                if (!cmd.TryGetBool("enabled", out var enabled))
                    return;

                var services = GetServices();
                if (services == null)
                    return;

                var settings = services.GetRequiredService<IPrivacySettingsService>();
                var updated = settings.UpdateContinuumLogging(enabled);
                if (updated)
                    SendSettingsSync(services, settings.GetSnapshot());
            }
            catch (Exception ex)
            {
                LogCommandFailure("continuum_logging", cmd, ex);
            }
        }

        public static void HandleSetPortalCapture(HostCommand cmd)
        {
            try
            {
                if (!cmd.TryGetBool("enabled", out var enabled))
                    return;

                var services = GetServices();
                if (services == null)
                    return;

                var settings = services.GetRequiredService<IPrivacySettingsService>();
                var updated = settings.UpdatePortalCapture(enabled);
                if (updated)
                    SendSettingsSync(services, settings.GetSnapshot());
            }
            catch (Exception ex)
            {
                LogCommandFailure("portal_capture", cmd, ex);
            }
        }

        public static void HandleOpenDataFolder(HostCommand cmd)
        {
            try
            {
                var services = GetServices();
                if (services == null)
                    return;

                var appPaths = services.GetRequiredService<IAppPaths>();
                var launcher = services.GetRequiredService<IProcessLauncher>();
                launcher.OpenFolder(appPaths.DataRoot);
            }
            catch (Exception ex)
            {
                LogCommandFailure("open_data_folder", cmd, ex);
            }
        }

        public static void HandleWipeData(HostCommand cmd)
        {
            try
            {
                var services = GetServices();
                if (services == null)
                    return;

                var wipeService = services.GetRequiredService<IDataWipeService>();
                var result = wipeService.WipeData();

                var toastHub = services.GetService<IToastHub>() ?? new ToastHubAdapter();
                var reason = ToastHub.ParseReason(cmd.TryGetString("reason", out var rawReason) ? rawReason : null, ToastReason.DockClick);

                if (result.Success)
                {
                    toastHub.TryShow(
                        ToastKey.DataWipeCompleted,
                        bypassLaunchQuiet: true,
                        origin: ToastOrigin.HostCommand,
                        reason: reason);
                }
                else
                {
                    toastHub.TryShow(
                        ToastKey.DataWipePartial,
                        bypassLaunchQuiet: true,
                        origin: ToastOrigin.HostCommand,
                        reason: reason);
                }
            }
            catch (Exception ex)
            {
                LogCommandFailure("wipe_data", cmd, ex);
                var services = GetServices();
                var toastHub = services?.GetService<IToastHub>() ?? new ToastHubAdapter();
                toastHub.TryShow(
                    ToastKey.DataWipePartial,
                    bypassLaunchQuiet: true,
                    origin: ToastOrigin.HostCommand,
                    reason: ToastReason.Background);
            }
        }

        private static void SendSettingsSync(IServiceProvider services, PrivacySettingsSnapshot snapshot)
        {
            try
            {
                var sender = services.GetRequiredService<IWebMessageSender>();
                var payload = System.Text.Json.JsonSerializer.SerializeToElement(new
                {
                    version = snapshot.Version,
                    continuumLoggingEnabled = snapshot.ContinuumLoggingEnabled,
                    portalCaptureEnabled = snapshot.PortalCaptureEnabled
                });

                sender.Send(new MessageEnvelope
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
                ValLog.Warn(nameof(PrivacyCommandHandlers), "Failed to send privacy settings sync.");
            }
        }

        private static IServiceProvider? GetServices()
        {
            return (Application.Current as App)?.Services;
        }

        private static void LogCommandFailure(string action, HostCommand cmd, Exception ex)
        {
            var key = $"cmd.fail.privacy.{action}";
            if (!RateLimiter.Allow(key, LogInterval))
                return;

            var sourceHost = cmd.SourceUri?.Host ?? "unknown";
            ValLog.Warn(nameof(PrivacyCommandHandlers),
                $"Privacy command failed ({action}) for {cmd.Type} (source: {sourceHost}). {LogSanitizer.Sanitize(ex.Message)}");
        }
    }
}
