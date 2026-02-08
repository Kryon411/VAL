using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VAL.Contracts
{
    public sealed record DockModel
    {
        [JsonPropertyName("version")]
        public string? Version { get; init; }

        [JsonPropertyName("portalBadge")]
        public DockBadge? PortalBadge { get; init; }

        [JsonPropertyName("sections")]
        public IReadOnlyList<DockSection> Sections { get; init; } = new List<DockSection>();

        [JsonPropertyName("advancedSections")]
        public IReadOnlyList<DockSection> AdvancedSections { get; init; } = new List<DockSection>();

        [JsonPropertyName("status")]
        public DockStatus? Status { get; init; }
    }

    public sealed record DockBadge
    {
        [JsonPropertyName("label")]
        public string? Label { get; init; }

        [JsonPropertyName("count")]
        public int? Count { get; init; }

        [JsonPropertyName("active")]
        public bool Active { get; init; }
    }

    public sealed record DockStatus
    {
        [JsonPropertyName("text")]
        public string? Text { get; init; }
    }

    public sealed record DockSection
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("title")]
        public string? Title { get; init; }

        [JsonPropertyName("subtitle")]
        public string? Subtitle { get; init; }

        [JsonPropertyName("headerControl")]
        public DockItem? HeaderControl { get; init; }

        [JsonPropertyName("blocks")]
        public IReadOnlyList<DockBlock> Blocks { get; init; } = new List<DockBlock>();
    }

    public sealed record DockBlock
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("type")]
        public string? Type { get; init; }

        [JsonPropertyName("className")]
        public string? ClassName { get; init; }

        [JsonPropertyName("text")]
        public string? Text { get; init; }

        [JsonPropertyName("items")]
        public IReadOnlyList<DockItem>? Items { get; init; }
    }

    public sealed record DockItem
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("type")]
        public string? Type { get; init; }

        [JsonPropertyName("label")]
        public string? Label { get; init; }

        [JsonPropertyName("kind")]
        public string? Kind { get; init; }

        [JsonPropertyName("state")]
        public bool? State { get; init; }

        [JsonPropertyName("disabled")]
        public bool Disabled { get; init; }

        [JsonPropertyName("disabledReason")]
        public string? DisabledReason { get; init; }

        [JsonPropertyName("tooltip")]
        public string? Tooltip { get; init; }

        [JsonPropertyName("count")]
        public int? Count { get; init; }

        [JsonPropertyName("max")]
        public int? Max { get; init; }

        [JsonPropertyName("command")]
        public DockCommand? Command { get; init; }

        [JsonPropertyName("localStateKey")]
        public string? LocalStateKey { get; init; }
    }

    public sealed record DockCommand
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("payload")]
        public JsonElement? Payload { get; init; }

        [JsonPropertyName("requiresChatId")]
        public bool RequiresChatId { get; init; }
    }
}
