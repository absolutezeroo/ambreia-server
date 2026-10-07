using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Vortex.Rpg.Content;

/// <summary>
/// The one JSON shape of a content document (spec Contenu §4.2), strict like <c>ZoneJson</c>: a
/// document is a contract, and a schema is generated from these options.
/// </summary>
public static class ContentJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        // The schema exporter refuses options without an explicit resolver.
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        NumberHandling = JsonNumberHandling.Strict,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        // "on_pickup", "dungeon_entry": the spec's spelling of every enumerated value; never a
        // number, which would read as a member that does not exist.
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false),
            new StrictStringListConverter(),
        },
        WriteIndented = false,
    };

    /// <exception cref="JsonException">Malformed JSON, a missing required field, an unknown field, a
    /// null where none is allowed, or a number written as text.</exception>
    public static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options)
        ?? throw new JsonException("The document is null.");

    /// <summary>Serializes by the runtime type: through <see cref="Definitions.ContentDefinition"/>
    /// alone, only its own three fields would be written.</summary>
    public static string Serialize(Definitions.ContentDefinition definition) =>
        JsonSerializer.Serialize(definition, definition.GetType(), Options);
}
