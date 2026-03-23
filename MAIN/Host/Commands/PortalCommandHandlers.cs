using System;
using VAL.Contracts;
using VAL.Host.Logging;
using VAL.Host.Services;

namespace VAL.Host.Commands
{
    internal sealed class PortalCommandHandlers : ICommandRegistryContributor
    {
        private static readonly string[] RequiredEnabled = { "enabled" };
        private readonly IPortalRuntimeStateManager _runtimeStateManager;
        private readonly RateLimiter _rateLimiter = new();
        private static readonly TimeSpan LogInterval = TimeSpan.FromSeconds(10);

        public PortalCommandHandlers(IPortalRuntimeStateManager runtimeStateManager)
        {
            _runtimeStateManager = runtimeStateManager ?? throw new ArgumentNullException(nameof(runtimeStateManager));
        }

        public void Register(CommandRegistry registry)
        {
            ArgumentNullException.ThrowIfNull(registry);

            registry.Register(new CommandSpec(
                WebCommandNames.PortalCommandSetEnabled,
                "Portal",
                RequiredEnabled,
                HandleSetEnabled));
            registry.Register(new CommandSpec(
                WebCommandNames.PortalCommandOpenSnip,
                "Portal",
                Array.Empty<string>(),
                HandleOpenSnip));
            registry.Register(new CommandSpec(
                WebCommandNames.PortalCommandOpenSnipOverlay,
                "Portal",
                Array.Empty<string>(),
                HandleOpenSnip));
            registry.Register(new CommandSpec(
                WebCommandNames.PortalCommandSendStaged,
                "Portal",
                Array.Empty<string>(),
                HandleSendStaged));
            registry.Register(new CommandSpec(
                WebCommandNames.PortalCommandSend,
                "Portal",
                Array.Empty<string>(),
                HandleSendStaged));
            registry.Register(new CommandSpec(
                WebCommandNames.PortalCommandSendStagedLegacy,
                "Portal",
                Array.Empty<string>(),
                HandleSendStaged));
        }

        public void HandleSetEnabled(HostCommand cmd)
        {
            try
            {
                if (cmd.TryGetBool("enabled", out var en))
                {
                    _runtimeStateManager.SetEnabled(en);
                }
            }
            catch (Exception ex)
            {
                LogCommandFailure("set_enabled", cmd, ex);
            }
        }

        public void HandleOpenSnip(HostCommand cmd)
        {
            try
            {
                _runtimeStateManager.OpenSnipOverlay();
            }
            catch (Exception ex)
            {
                LogCommandFailure("open_snip", cmd, ex);
            }
        }

        public void HandleSendStaged(HostCommand cmd)
        {
            try
            {
                int max = 10;
                if (cmd.TryGetInt("max", out var m)) max = m;
                _runtimeStateManager.SendStaged(max);
            }
            catch (Exception ex)
            {
                LogCommandFailure("send_staged", cmd, ex);
            }
        }

        private void LogCommandFailure(string action, HostCommand cmd, Exception ex)
        {
            var key = $"cmd.fail.portal.{action}";
            if (!_rateLimiter.Allow(key, LogInterval))
                return;

            var sourceHost = cmd.SourceUri?.Host ?? "unknown";
            ValLog.Warn(nameof(PortalCommandHandlers),
                $"Portal command failed ({action}) for {cmd.Type} (source: {sourceHost}). {ex.GetType().Name}: {LogSanitizer.Sanitize(ex.Message)}");
        }
    }
}
