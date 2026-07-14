namespace VAL.Continuum
{
    internal static class ContinuumSeedClassifier
    {
        private static readonly string[] Markers =
        {
            "CONTEXT BLOCK -- READ ONLY",
            "CONTEXT BLOCK \u2014 READ ONLY",
            "ESSENCE-M SNAPSHOT (AUTHORITATIVE)",
            "ESSENCE\u2011M SNAPSHOT (AUTHORITATIVE)",
            "WHERE WE LEFT OFF -- LAST COMPLETE EXCHANGE (AUTHORITATIVE)",
            "WHERE WE LEFT OFF \u2014 LAST COMPLETE EXCHANGE (AUTHORITATIVE)",
            "CONTEXT FILLER (REFERENCE ONLY -- DO NOT ADVANCE FROM HERE)",
            "CONTEXT FILLER (REFERENCE ONLY \u2014 DO NOT ADVANCE FROM HERE)",
        };

        public static bool IsContinuumSeed(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return Markers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));
        }
    }
}
