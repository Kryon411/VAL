using System;

namespace VAL.Continuum.Pipeline.Truth
{
    internal static class TruthLine
    {
        internal static bool TryParse(string line, out char role, out string payload)
        {
            role = '\0';
            payload = string.Empty;

            if (string.IsNullOrWhiteSpace(line))
                return false;

            if (line.Length < 2 || line[1] != '|')
                return false;

            var rc = char.ToUpperInvariant(line[0]);
            if (rc != 'A' && rc != 'U')
                return false;

            role = rc;
            payload = line.Length > 2 ? line.Substring(2) : string.Empty;
            return true;
        }
    }
}
