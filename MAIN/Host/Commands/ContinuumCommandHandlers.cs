using System;
using VAL.Contracts;
using VAL.Continuum;
using VAL.Host.Logging;

namespace VAL.Host.Commands
{
    internal sealed class ContinuumCommandHandlers : ICommandRegistryContributor
    {
        private static readonly string[] ContinuumTypes =
        {
            WebCommandNames.ContinuumCaptureFlushAck,
            WebCommandNames.ContinuumSessionAttach,
            WebCommandNames.ContinuumSessionAttached,
            WebCommandNames.ContinuumCommandToggleLogging,
            WebCommandNames.ContinuumUiComposerInteraction,
            WebCommandNames.ContinuumTruthAppend,
            WebCommandNames.TruthAppend,
            WebCommandNames.ContinuumTruth,
            WebCommandNames.ContinuumCommandPulse,
            WebCommandNames.ContinuumCommandRefreshQuick,
            WebCommandNames.ContinuumCommandOpenSessionFolder,
            WebCommandNames.ContinuumCommandChronicleCancel,
            WebCommandNames.ContinuumCommandCancelChronicle,
            WebCommandNames.ContinuumCommandChronicleRebuildTruth,
            WebCommandNames.ContinuumCommandChronicle,
            WebCommandNames.ContinuumChronicleProgress,
            WebCommandNames.ContinuumChronicleDone,
            WebCommandNames.InjectSuccess,
            WebCommandNames.ContinuumEvent,
        };
        private static readonly TimeSpan LogInterval = TimeSpan.FromSeconds(10);
        private readonly ContinuumHost _continuumHost;
        private readonly ILog _log;
        private readonly RateLimiter _rateLimiter = new();

        public ContinuumCommandHandlers(ContinuumHost continuumHost, ILog log)
        {
            _continuumHost = continuumHost ?? throw new ArgumentNullException(nameof(continuumHost));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public void Register(CommandRegistry registry)
        {
            ArgumentNullException.ThrowIfNull(registry);

            foreach (var type in ContinuumTypes)
            {
                registry.Register(new CommandSpec(
                    type,
                    "Continuum",
                    Array.Empty<string>(),
                    HandleContinuumCommand));
            }
        }

        public void HandleContinuumCommand(HostCommand cmd)
        {
            try
            {
                _continuumHost.HandleCommand(cmd);
            }
            catch (Exception ex)
            {
                LogHandlerFailure("cmd.fail.continuum", cmd, ex);
            }
        }

        private void LogHandlerFailure(string key, HostCommand cmd, Exception ex)
        {
            if (!_rateLimiter.Allow(key, LogInterval))
                return;

            var sourceHost = cmd.SourceUri?.Host ?? "unknown";
            _log.Warn(nameof(ContinuumCommandHandlers),
                $"Command handler failed for {cmd.Type} (source: {sourceHost}). {ex.GetType().Name}: {LogSanitizer.Sanitize(ex.Message)}");
        }
    }
}
