using System;

namespace VAL.Host.Security
{
    internal static class WebMessageOriginGuard
    {
        internal static readonly string[] AllowedHosts = new[]
        {
            "chatgpt.com",
            "chat.openai.com"
        };

        internal static bool TryIsAllowed(string? source, out Uri? uri)
        {
            uri = null;
            if (string.IsNullOrWhiteSpace(source))
                return false;

            if (!Uri.TryCreate(source, UriKind.Absolute, out var parsed))
                return false;

            if (!string.Equals(parsed.Scheme, "https", StringComparison.OrdinalIgnoreCase))
                return false;

            var host = parsed.Host?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(host))
                return false;

            foreach (var allowed in AllowedHosts)
            {
                if (string.Equals(host, allowed, StringComparison.OrdinalIgnoreCase))
                {
                    uri = parsed;
                    return true;
                }
            }

            return false;
        }
    }
}
