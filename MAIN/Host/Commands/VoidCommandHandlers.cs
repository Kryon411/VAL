using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;

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

                if (enabled)
                    toasts.TryShow(ToastKey.VoidEnabled);
                else
                    toasts.TryShow(ToastKey.VoidDisabled);
            }
        }

        private static IToastHub? GetToastHub()
        {
            return (Application.Current as App)?.Services.GetService<IToastHub>();
        }
    }
}
