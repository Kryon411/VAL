using System;
using VAL.Continuum;
using VAL.Host.Logging;

namespace VAL.Host.Commands
{
    internal static class ContinuumCommandHandlers
    {
        private static readonly RateLimiter RateLimiter = new();
        private static readonly TimeSpan LogInterval = TimeSpan.FromSeconds(10);

        public static void HandleContinuumCommand(HostCommand cmd)
        {
            try
            {
                ContinuumHost.HandleJson(cmd.RawJson);
            }
            catch (Exception ex)
            {
                LogHandlerFailure("cmd.fail.continuum", cmd, ex);
            }
        }

        private static void LogHandlerFailure(string key, HostCommand cmd, Exception ex)
        {
            if (!RateLimiter.Allow(key, LogInterval))
                return;

            var sourceHost = cmd.SourceUri?.Host ?? "unknown";
            ValLog.Warn(nameof(ContinuumCommandHandlers),
                $"Command handler failed for {cmd.Type} (source: {sourceHost}). {ex.GetType().Name}: {LogSanitizer.Sanitize(ex.Message)}");
        }
    }
}
