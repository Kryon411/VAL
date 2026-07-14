using System.Text.Json;
using System.Text.Json.Serialization;

using VAL.Host.Commands;

namespace VAL.Continuum
{
    internal sealed class ContinuumCommandMessage
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("chatId")]
        public string? ChatId { get; set; }

        [JsonPropertyName("requestId")]
        public string? RequestId { get; set; }

        [JsonPropertyName("role")]
        public string? Role { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("line")]
        public string? Line { get; set; }

        [JsonPropertyName("evt")]
        public string? Event { get; set; }

        [JsonPropertyName("reason")]
        public string? Reason { get; set; }

        [JsonPropertyName("capturedTurns")]
        public int? CapturedTurns { get; set; }

        [JsonPropertyName("ms")]
        public long? ElapsedMilliseconds { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }

        [JsonPropertyName("enabled")]
        public bool? Enabled { get; set; }

        public static ContinuumCommandMessage From(HostCommand command)
        {
            ContinuumCommandMessage? message = null;
            if (command.Root.ValueKind == JsonValueKind.Object)
            {
                try
                {
                    message = command.Root.Deserialize<ContinuumCommandMessage>();
                }
                catch (JsonException)
                {
                    // Malformed optional fields fall back to envelope metadata.
                }
            }

            message ??= new ContinuumCommandMessage();
            if (string.IsNullOrWhiteSpace(message.Type))
                message.Type = command.Type;
            if (string.IsNullOrWhiteSpace(message.ChatId))
                message.ChatId = command.ChatId;

            return message;
        }
    }
}
