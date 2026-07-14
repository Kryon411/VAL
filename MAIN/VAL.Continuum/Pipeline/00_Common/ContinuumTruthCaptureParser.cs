namespace VAL.Continuum
{
    internal static class ContinuumTruthCaptureParser
    {
        public static char ParseRole(string? role)
        {
            return role?.Trim().ToLowerInvariant() switch
            {
                "a" or "assistant" => 'A',
                _ => 'U',
            };
        }

        public static bool TryParseLegacyLine(string? line, out char role, out string text)
        {
            role = 'U';
            text = string.Empty;

            if (string.IsNullOrWhiteSpace(line))
                return false;

            var parts = line.Split(']');
            if (parts.Length >= 3)
            {
                var tag = parts[1].Replace("[", string.Empty, StringComparison.Ordinal).Trim();
                role = ParseRole(tag);
            }

            var payloadSeparator = line.LastIndexOf("] ", StringComparison.Ordinal);
            if (payloadSeparator >= 0 && payloadSeparator + 2 < line.Length)
            {
                text = line[(payloadSeparator + 2)..];
            }
            else
            {
                var thirdBracket = FindOccurrence(line, ']', 3);
                text = thirdBracket >= 0 && thirdBracket + 1 < line.Length
                    ? line[(thirdBracket + 1)..].TrimStart()
                    : line;
            }

            return !string.IsNullOrWhiteSpace(text);
        }

        private static int FindOccurrence(string value, char character, int occurrence)
        {
            var count = 0;
            for (var index = 0; index < value.Length; index++)
            {
                if (value[index] != character)
                    continue;

                count++;
                if (count == occurrence)
                    return index;
            }

            return -1;
        }
    }
}
