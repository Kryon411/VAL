using System;

namespace VAL.Host.Security
{
    internal static class WebMessageOriginGuard
    {
        internal static bool TryIsAllowed(string? source, out Uri? uri)
        {
            return WebOriginPolicy.TryIsBridgeAllowed(source, out uri);
        }
    }
}
