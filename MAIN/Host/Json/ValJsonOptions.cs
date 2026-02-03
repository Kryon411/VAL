using System.Text.Json;

namespace VAL.Host.Json
{
    internal static class ValJsonOptions
    {
        public static readonly JsonSerializerOptions Default = new()
        {
            WriteIndented = true
        };

        public static readonly JsonSerializerOptions CaseInsensitive = new()
        {
            PropertyNameCaseInsensitive = true
        };
    }
}
