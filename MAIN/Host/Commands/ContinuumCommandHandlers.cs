using System;
using VAL.Continuum;
using VAL.Host.Logging;

namespace VAL.Host.Commands
{
    internal sealed class ContinuumCommandHandlers
    {
        private static readonly TimeSpan LogInterval = TimeSpan.FromSeconds(10);
        private readonly ContinuumHost _continuumHost;
        private readonly RateLimiter _rateLimiter = new();

        public ContinuumCommandHandlers(ContinuumHost continuumHost)
        {
            _continuumHost = continuumHost ?? throw new ArgumentNullException(nameof(continuumHost));
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
            ValLog.Warn(nameof(ContinuumCommandHandlers),
                $"Command handler failed for {cmd.Type} (source: {sourceHost}). {ex.GetType().Name}: {LogSanitizer.Sanitize(ex.Message)}");
        }
    }
}
