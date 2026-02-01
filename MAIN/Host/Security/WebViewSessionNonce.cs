using System;
using System.Security.Cryptography;

namespace VAL.Host.Security
{
    internal interface IWebViewSessionNonce
    {
        string Value { get; }
    }

    internal sealed class WebViewSessionNonce : IWebViewSessionNonce
    {
        public string Value { get; } = GenerateNonce();

        private static string GenerateNonce()
        {
            Span<byte> bytes = stackalloc byte[32];
            RandomNumberGenerator.Fill(bytes);
            return Convert.ToBase64String(bytes);
        }
    }
}
