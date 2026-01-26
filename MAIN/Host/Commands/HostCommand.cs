using System;
using System.Text.Json;

namespace VAL.Host.Commands
{
    /// <summary>
    /// Represents a single WebView -> Host command.
    ///
    /// IMPORTANT: Root (JsonElement) is only valid during the dispatch call.
    /// Handlers must not store it for later use.
    /// </summary>
    public readonly struct HostCommand
    {
        public string Type { get; }
        public string RawJson { get; }
        public string? ChatId { get; }
        public Uri SourceUri { get; }
        public JsonElement Root { get; }

        internal HostCommand(string type, string rawJson, string? chatId, Uri sourceUri, JsonElement root)
        {
            Type = type;
            RawJson = rawJson;
            ChatId = chatId;
            SourceUri = sourceUri;
            Root = root;
        }

        public bool TryGetString(string name, out string? value)
        {
            value = null;
            try
            {
                if (!Root.TryGetProperty(name, out var el))
                    return false;

                if (el.ValueKind == JsonValueKind.String)
                {
                    value = el.GetString();
                    return !string.IsNullOrWhiteSpace(value);
                }

                // Allow primitive values to be stringified (schema-lite).
                if (el.ValueKind == JsonValueKind.Number || el.ValueKind == JsonValueKind.True || el.ValueKind == JsonValueKind.False)
                {
                    value = el.ToString();
                    return !string.IsNullOrWhiteSpace(value);
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        public bool TryGetBool(string name, out bool value)
        {
            value = false;
            try
            {
                if (!Root.TryGetProperty(name, out var el))
                    return false;

                if (el.ValueKind == JsonValueKind.True)
                {
                    value = true;
                    return true;
                }

                if (el.ValueKind == JsonValueKind.False)
                {
                    value = false;
                    return true;
                }

                if (el.ValueKind == JsonValueKind.String)
                {
                    var s = el.GetString();
                    if (!string.IsNullOrWhiteSpace(s) && bool.TryParse(s, out var parsed))
                    {
                        value = parsed;
                        return true;
                    }
                }

                if (el.ValueKind == JsonValueKind.Number)
                {
                    if (el.TryGetInt32(out var i))
                    {
                        value = i != 0;
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        public bool TryGetInt(string name, out int value)
        {
            value = 0;
            try
            {
                if (!Root.TryGetProperty(name, out var el))
                    return false;

                if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var i))
                {
                    value = i;
                    return true;
                }

                if (el.ValueKind == JsonValueKind.String)
                {
                    var s = el.GetString();
                    if (!string.IsNullOrWhiteSpace(s) && int.TryParse(s, out var parsed))
                    {
                        value = parsed;
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}
