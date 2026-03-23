using System;
using VAL.Contracts;

namespace VAL.Host.Commands
{
    internal sealed class VoidCommandHandlers : ICommandRegistryContributor
    {
        private static readonly string[] RequiredEnabled = { "enabled" };
        private bool? _lastEnabledState;
        private readonly IToastHub _toastHub;

        public VoidCommandHandlers(IToastHub toastHub)
        {
            _toastHub = toastHub ?? throw new ArgumentNullException(nameof(toastHub));
        }

        public void Register(CommandRegistry registry)
        {
            ArgumentNullException.ThrowIfNull(registry);

            registry.Register(new CommandSpec(
                WebCommandNames.VoidCommandSetEnabled,
                "Void",
                RequiredEnabled,
                HandleSetEnabled));
        }

        public void HandleSetEnabled(HostCommand cmd)
        {
            // Default false when the field is missing or malformed.
            var enabled = false;
            if (cmd.TryGetBool("enabled", out var parsed))
                enabled = parsed;

            // Cooldown: only toast once per state transition.
            if (_lastEnabledState != enabled)
            {
                _lastEnabledState = enabled;
                var reason = ToastReasonParser.Parse(cmd.TryGetString("reason", out var rawReason) ? rawReason : null, ToastReason.DockClick);

                if (enabled)
                    _toastHub.TryShow(ToastKey.VoidEnabled, origin: ToastOrigin.HostCommand, reason: reason);
                else
                    _toastHub.TryShow(ToastKey.VoidDisabled, origin: ToastOrigin.HostCommand, reason: reason);
            }
        }
    }
}
