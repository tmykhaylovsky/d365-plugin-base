using System.Text.Json.Serialization;

using System.Text.Json;

namespace Ops.Plugins.Tools.Models;

public sealed class ScriptCatalog
{
    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("scripts")]
    public List<CatalogAction> Scripts { get; set; } = [];
}

public sealed class CatalogAction
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("actionKind")]
    public string ActionKind { get; set; } = "script";

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("script")]
    public string? Script { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("dangerLevel")]
    public string DangerLevel { get; set; } = "read";

    [JsonPropertyName("displayOrder")]
    public int DisplayOrder { get; set; } = 100;

    [JsonPropertyName("defaultDryRun")]
    public bool DefaultDryRun { get; set; }

    [JsonPropertyName("requiresConfirmation")]
    public bool RequiresConfirmation { get; set; }

    [JsonPropertyName("requiresConfirmationWhen")]
    public List<string> RequiresConfirmationWhen { get; set; } = [];

    [JsonPropertyName("parameters")]
    public List<CatalogParameter> Parameters { get; set; } = [];

    [JsonIgnore]
    public string DisplayTitle => Title.Length == 0 ? Id : Title;

    public override string ToString() => DisplayTitle;
}

public sealed class CatalogParameter
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("defaultValue")]
    [JsonConverter(typeof(JsonScalarStringConverter))]
    public string? DefaultValue { get; set; }

    [JsonPropertyName("advanced")]
    public bool Advanced { get; set; }

    [JsonPropertyName("requiresConfirmation")]
    public bool RequiresConfirmation { get; set; }

    [JsonPropertyName("confirmationRequired")]
    public bool ConfirmationRequired { get; set; }

    [JsonPropertyName("choices")]
    public List<string> Choices { get; set; } = [];
}

public sealed class JsonScalarStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.True => bool.TrueString.ToLowerInvariant(),
            JsonTokenType.False => bool.FalseString.ToLowerInvariant(),
            JsonTokenType.Number => ReadNumber(ref reader),
            JsonTokenType.Null => null,
            _ => throw new JsonException($"Expected a scalar value, but found {reader.TokenType}.")
        };
    }

    private static string ReadNumber(ref Utf8JsonReader reader)
    {
        return reader.TryGetInt64(out var integer)
            ? integer.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : reader.GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(value);
    }
}
