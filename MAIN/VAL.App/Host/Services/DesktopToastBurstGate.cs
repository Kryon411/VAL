using System;
using System.Collections.Generic;
using System.Linq;

namespace VAL.App.Host.Services
{
    internal sealed class DesktopToastBurstGate
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, DateTime> _lastSeenUtc = new(StringComparer.Ordinal);
        private readonly TimeSpan _window;

        public DesktopToastBurstGate(TimeSpan window)
        {
            _window = window;
        }

        public bool ShouldSuppress(string? title, string? subtitle)
        {
            return ShouldSuppress(title, subtitle, DateTime.UtcNow);
        }

        internal bool ShouldSuppress(string? title, string? subtitle, DateTime nowUtc)
        {
            var key = CreateKey(title, subtitle);

            lock (_gate)
            {
                var staleKeys = _lastSeenUtc
                    .Where(entry => (nowUtc - entry.Value) > _window)
                    .Select(entry => entry.Key)
                    .ToList();

                foreach (var staleKey in staleKeys)
                {
                    _lastSeenUtc.Remove(staleKey);
                }

                if (_lastSeenUtc.TryGetValue(key, out var lastSeenUtc) &&
                    (nowUtc - lastSeenUtc) <= _window)
                {
                    return true;
                }

                _lastSeenUtc[key] = nowUtc;
                return false;
            }
        }

        private static string CreateKey(string? title, string? subtitle)
        {
            return (title ?? string.Empty).Trim() + "\n" + (subtitle ?? string.Empty).Trim();
        }
    }
}
