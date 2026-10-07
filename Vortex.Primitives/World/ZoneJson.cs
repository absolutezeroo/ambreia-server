using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Vortex.Primitives.World;

/// <summary>The one JSON shape of a zone document, shared by the server, the tests and the schema.</summary>
public static class ZoneJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        // The schema exporter refuses options without an explicit resolver.
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        // The web defaults read "12" as 12; a zone document is a contract, and the editor's types
        // are generated from its schema, so a number is a number.
        NumberHandling = JsonNumberHandling.Strict,
        // JSON objects have no order: an editor may write a condition's "kind" after its fields.
        AllowOutOfOrderMetadataProperties = true,
        // "decor": null is a malformed document, refused here rather than crashing the validator.
        RespectNullableAnnotations = true,
        Converters = { new JsonStringEnumConverter() },
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = false,
    };

    public static string Serialize(ZoneDocument document) =>
        JsonSerializer.Serialize(document, Options);

    /// <exception cref="JsonException">Malformed JSON, a missing required field, an unknown
    /// <c>kind</c> or an unknown property.</exception>
    public static ZoneDocument Deserialize(string json) =>
        JsonSerializer.Deserialize<ZoneDocument>(json, Options)
        ?? throw new JsonException("The document is null.");
}
