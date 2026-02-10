using System;

namespace VAL.Host.Security
{
    public static class WebMessageOriginGuard
    {
        public static bool TryIsAllowed(
            string? source,
            string? nonce,
            string expectedNonce,
            out Uri? uri,
            out string? rejectReason)
        {
            rejectReason = null;
            if (!WebOriginPolicy.TryIsBridgeAllowed(source, out uri))
            {
                rejectReason = "origin_not_allowlisted";
                return false;
            }

            if (string.IsNullOrWhiteSpace(expectedNonce))
            {
                rejectReason = "nonce_uninitialized";
                uri = null;
                return false;
            }

            if (string.IsNullOrWhiteSpace(nonce))
            {
                rejectReason = "nonce_missing";
                uri = null;
                return false;
            }

            if (!string.Equals(nonce, expectedNonce, StringComparison.Ordinal))
            {
                rejectReason = "nonce_mismatch";
                uri = null;
                return false;
            }

            return true;
        }
    }
}
