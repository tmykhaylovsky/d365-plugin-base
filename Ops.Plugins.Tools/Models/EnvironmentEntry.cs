using System.Text.Json.Serialization;

namespace Ops.Plugins.Tools.Models;

public sealed class EnvironmentEntry
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("lastUsedUtc")]
    public DateTimeOffset LastUsedUtc { get; set; }

    public override string ToString() => string.IsNullOrWhiteSpace(Name) ? Url : $"{Name} ({Url})";
}
