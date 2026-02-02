using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using VAL.Contracts;
using VAL.Host;

namespace VAL.Host.WebMessaging
{
    public sealed class MessageEnvelope
    {
        [JsonPropertyName("type")]
        public string? Type { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("payload")]
        public JsonElement? Payload { get; init; }

        [JsonPropertyName("chatId")]
        public string? ChatId { get; init; }

        [JsonPropertyName("source")]
        public string? Source { get; init; }

        [JsonPropertyName("nonce")]
        public string? Nonce { get; init; }

        [JsonPropertyName("ts")]
        public long? Ts { get; init; }

        public static bool TryParse(string json, out MessageEnvelope envelope)
        {
            envelope = null!;

            if (string.IsNullOrWhiteSpace(json))
            {
                ValLog.Warn(nameof(MessageEnvelope), "Parse failed: empty JSON payload.");
                return false;
            }

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.ValueKind != JsonValueKind.Object)
                {
                    ValLog.Warn(nameof(MessageEnvelope), "Parse failed: root is not an object.");
                    return false;
                }

                var type = ReadString(root, "type");
                if (string.IsNullOrWhiteSpace(type))
                {
                    ValLog.Warn(nameof(MessageEnvelope), "Parse failed: missing type.");
                    return false;
                }

                var name = ReadString(root, "name");
                var chatId = ReadString(root, "chatId");
                var source = ReadString(root, "source");
                var nonce = ReadString(root, "nonce");
                var ts = ReadLong(root, "ts");
                JsonElement? payload = null;

                if (root.TryGetProperty("payload", out var payloadEl))
                    payload = payloadEl.Clone();

                if (!IsEnvelopeType(type))
                {
                    name = type.Trim();
                    type = WebMessageTypes.Command;
                    if (!payload.HasValue)
                        payload = root.Clone();
                }

                envelope = new MessageEnvelope
                {
                    Type = type?.Trim(),
                    Name = name?.Trim(),
                    Payload = payload,
                    ChatId = chatId,
                    Source = source,
                    Nonce = nonce,
                    Ts = ts
                };

                return true;
            }
            catch (JsonException)
            {
                ValLog.Warn(nameof(MessageEnvelope), "Parse failed: invalid JSON.");
                return false;
            }
            catch (Exception)
            {
                ValLog.Warn(nameof(MessageEnvelope), "Parse failed: unexpected error.");
                return false;
            }
        }

        private static bool IsEnvelopeType(string? type)
        {
            if (string.IsNullOrWhiteSpace(type))
                return false;

            return type.Equals(WebMessageTypes.Command, StringComparison.OrdinalIgnoreCase)
                || type.Equals(WebMessageTypes.Event, StringComparison.OrdinalIgnoreCase)
                || type.Equals(WebMessageTypes.Log, StringComparison.OrdinalIgnoreCase);
        }

        private static string? ReadString(JsonElement root, string name)
        {
            try
            {
                if (!root.TryGetProperty(name, out var el))
                    return null;

                return el.ValueKind == JsonValueKind.String ? el.GetString() : null;
            }
            catch
            {
                return null;
            }
        }

        private static long? ReadLong(JsonElement root, string name)
        {
            try
            {
                if (!root.TryGetProperty(name, out var el))
                    return null;

                if (el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out var value))
                    return value;

                if (el.ValueKind == JsonValueKind.String && long.TryParse(el.GetString(), out var parsed))
                    return parsed;
            }
            catch
            {
                return null;
            }

            return null;
        }
    }
}
