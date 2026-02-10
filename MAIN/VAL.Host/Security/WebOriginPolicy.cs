using System;

namespace VAL.Host.Security
{
    public static class WebOriginPolicy
    {
        private static readonly string[] BridgeAllowedHosts =
        {
            "chatgpt.com",
            "chat.openai.com"
        };

        private static readonly string[] NavigationAllowedHosts =
        {
            "chatgpt.com",
            "chat.openai.com",
            "auth.openai.com",
            "openai.com",
            "accounts.google.com",
            "appleid.apple.com",
            "login.microsoftonline.com"
        };

        public static bool TryIsBridgeAllowed(string? uri, out Uri? parsed)
        {
            return TryIsAllowed(uri, BridgeAllowedHosts, out parsed);
        }

        public static bool TryIsNavigationAllowed(string? uri, out Uri? parsed)
        {
            return TryIsAllowed(uri, NavigationAllowedHosts, out parsed);
        }

        private static bool TryIsAllowed(string? uri, string[] allowedHosts, out Uri? parsed)
        {
            parsed = null;
            if (string.IsNullOrWhiteSpace(uri))
                return false;

            if (!Uri.TryCreate(uri, UriKind.Absolute, out var candidate))
                return false;

            if (!string.Equals(candidate.Scheme, "https", StringComparison.OrdinalIgnoreCase))
                return false;

            var host = candidate.Host?.Trim();
            if (string.IsNullOrEmpty(host))
                return false;

            foreach (var allowed in allowedHosts)
            {
                if (string.Equals(host, allowed, StringComparison.OrdinalIgnoreCase))
                {
                    parsed = candidate;
                    return true;
                }
            }

            return false;
        }
    }
}
