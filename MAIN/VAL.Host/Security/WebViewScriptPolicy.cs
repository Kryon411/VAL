using System;
using System.Text.Json;

namespace VAL.Host.Security;

internal static class WebViewScriptPolicy
{
    internal const string GuardTemplate = """
        (() => {
          if (window !== window.top ||
              !['https://chatgpt.com','https://chat.openai.com'].includes(window.location.origin)) return;
          let path;
          try {
            const escapedPath = window.location.pathname;
            if (/%2f|%5c/i.test(escapedPath)) return;
            path = decodeURIComponent(escapedPath).toLowerCase();
          } catch { return; }
          if (path.includes('%') || path.includes('\\')) return;
          const authenticationPaths = __VAL_AUTH_PATHS__;
          if (authenticationPaths.some(prefix => path === prefix || path.startsWith(prefix + '/'))) return;
        __VAL_SCRIPT__
        })();
        """;

    internal static string Guard(string script)
    {
        return GuardTemplate.Replace("__VAL_AUTH_PATHS__", JsonSerializer.Serialize(WebOriginPolicy.AuthenticationPathPrefixes), StringComparison.Ordinal)
            .Replace("__VAL_SCRIPT__", script, StringComparison.Ordinal);
    }

    internal static string LogOrigin(string? source)
    {
        return Uri.TryCreate(source, UriKind.Absolute, out var uri)
            ? uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped)
            : "<invalid origin>";
    }
}
