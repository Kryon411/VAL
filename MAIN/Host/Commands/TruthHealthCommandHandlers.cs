using System;
using Microsoft.Extensions.DependencyInjection;
using VAL.Host.Logging;
using VAL.Host.Services;

namespace VAL.Host.Commands
{
    internal static class TruthHealthCommandHandlers
    {
        private static readonly RateLimiter RateLimiter = new();
        private static readonly TimeSpan LogInterval = TimeSpan.FromSeconds(10);

        public static void HandleOpen(HostCommand cmd)
        {
            try
            {
                var service = App.Services.GetService<ITruthHealthWindowService>();
                service?.ShowTruthHealth();
            }
            catch (Exception ex)
            {
                LogCommandFailure(cmd, ex);
            }
        }

        private static void LogCommandFailure(HostCommand cmd, Exception ex)
        {
            var key = "cmd.fail.truth.health.open";
            if (!RateLimiter.Allow(key, LogInterval))
                return;

            var sourceHost = cmd.SourceUri?.Host ?? "unknown";
            ValLog.Warn(nameof(TruthHealthCommandHandlers),
                $"Truth health command failed for {cmd.Type} (source: {sourceHost}). {ex.GetType().Name}: {LogSanitizer.Sanitize(ex.Message)}");
        }
    }
}
