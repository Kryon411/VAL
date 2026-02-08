using System;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace VAL.Host.Logging
{
    internal sealed class RateLimiter
    {
        private readonly ConcurrentDictionary<string, long> _lastTicks = new(StringComparer.Ordinal);

        internal bool Allow(string key, TimeSpan interval)
        {
            if (string.IsNullOrWhiteSpace(key))
                return true;

            if (interval <= TimeSpan.Zero)
                return true;

            var now = Stopwatch.GetTimestamp();
            var intervalTicks = ToStopwatchTicks(interval);
            var allowed = false;

            _lastTicks.AddOrUpdate(
                key,
                _ =>
                {
                    allowed = true;
                    return now;
                },
                (_, last) =>
                {
                    if (now - last < intervalTicks)
                    {
                        allowed = false;
                        return last;
                    }

                    allowed = true;
                    return now;
                });

            return allowed;
        }

        private static long ToStopwatchTicks(TimeSpan interval)
        {
            var ticks = interval.Ticks;
            return (long)(ticks * (double)Stopwatch.Frequency / TimeSpan.TicksPerSecond);
        }
    }
}
