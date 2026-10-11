using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using VAL.Host.Security;

namespace VAL.Host;

internal static class ModuleStylesheetScript
{
    internal const string InjectionTemplate = """
        (() => { try {
          const id = __VAL_STYLE_ID__;
          const css = __VAL_STYLE_CSS__;
          const add = () => { try {
            if (document.getElementById(id)) return;
            const s = document.createElement('style');
            s.id = id;
            s.textContent = css;
            const t = document.head || document.documentElement || document.body;
            if (t) t.appendChild(s);
          } catch {} };
          if (document.getElementById(id)) return;
          if (document.head || document.documentElement || document.body) { add(); }
          else if (document.readyState === 'loading') { document.addEventListener('DOMContentLoaded', add, { once: true }); }
          else { setTimeout(add, 0); }
        } catch {} })();
        """;

    internal static string GetStyleId(string moduleName, string relativePath)
    {
        var safeName = new string(moduleName
            .Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-')
            .ToArray());
        // Match the loader's case-insensitive path identity without collapsing distinct filenames.
        var identity = moduleName.ToUpperInvariant() + "\n" + relativePath.Replace('\\', '/').ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        return "val-style-" + safeName + "-" + hash;
    }

    internal static string Create(string moduleName, string relativePath, string css)
    {
        var script = InjectionTemplate
            .Replace("__VAL_STYLE_ID__", JsonSerializer.Serialize(GetStyleId(moduleName, relativePath)), StringComparison.Ordinal)
            .Replace("__VAL_STYLE_CSS__", JsonSerializer.Serialize(css), StringComparison.Ordinal);
        return WebViewScriptPolicy.Guard(script);
    }
}
