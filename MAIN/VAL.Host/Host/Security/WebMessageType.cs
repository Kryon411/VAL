using System;
using System.Text.Json;

namespace VAL.Host.Security
{
    internal static class WebMessageType
    {
        internal static bool TryGetType(string json, out string type)
        {
            type = string.Empty;

            if (string.IsNullOrWhiteSpace(json))
                return false;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    return false;

                if (!root.TryGetProperty("type", out var typeElement))
                    return false;

                if (typeElement.ValueKind != JsonValueKind.String)
                    return false;

                var value = typeElement.GetString();
                if (string.IsNullOrWhiteSpace(value))
                    return false;

                type = value;
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
