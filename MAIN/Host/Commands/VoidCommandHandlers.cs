using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using VAL.Host;

namespace VAL.Host.Commands
{
    internal static class VoidCommandHandlers
    {
        private static bool? _lastEnabledState;

        public static void HandleSetEnabled(HostCommand cmd)
        {
            // Default false when the field is missing or malformed.
            var enabled = false;
            if (cmd.TryGetBool("enabled", out var parsed))
                enabled = parsed;

            // Cooldown: only toast once per state transition.
            if (_lastEnabledState != enabled)
            {
                _lastEnabledState = enabled;
                var toasts = GetToastHub() ?? new ToastHubAdapter();
                var reason = ResolveReason(cmd);

                if (enabled)
                    toasts.TryShow(ToastKey.VoidEnabled, origin: ToastOrigin.HostCommand, reason: reason);
                else
                    toasts.TryShow(ToastKey.VoidDisabled, origin: ToastOrigin.HostCommand, reason: reason);
            }
        }

        private static IToastHub? GetToastHub()
        {
            return (Application.Current as App)?.Services.GetService<IToastHub>();
        }

        private static ToastReason ResolveReason(HostCommand cmd)
        {
            var host = cmd.SourceUri?.Host ?? string.Empty;
            if (host.Contains("hotkey", StringComparison.OrdinalIgnoreCase))
                return ToastReason.Hotkey;
            if (host.Contains("dock", StringComparison.OrdinalIgnoreCase))
                return ToastReason.DockClick;

            return ToastReason.DockClick;
        }
    }
}
