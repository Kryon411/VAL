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

        internal static readonly string[] AuthenticationPathPrefixes =
        {
            "/auth", "/api/auth", "/login", "/logout", "/signin", "/signup",
            "/sign-in", "/sign-up", "/oauth", "/oauth2"
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
            if (!TryIsAllowed(uri, BridgeAllowedHosts, out var candidate) ||
                !IsBridgePathAllowed(candidate!.AbsolutePath))
            {
                parsed = null;
                return false;
            }

            parsed = candidate;
            return true;
        }

        private static bool IsBridgePathAllowed(string escapedPath)
        {
            // Encoded separators and repeated escaping can have different router semantics.
            if (escapedPath.Contains("%2f", StringComparison.OrdinalIgnoreCase) ||
                escapedPath.Contains("%5c", StringComparison.OrdinalIgnoreCase))
                return false;

            var path = Uri.UnescapeDataString(escapedPath);
            if (path.Contains('%') || path.Contains('\\'))
                return false;

            foreach (var prefix in AuthenticationPathPrefixes)
            {
                if (path.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            return true;
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

            if (!candidate.IsDefaultPort || !string.IsNullOrEmpty(candidate.UserInfo))
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
