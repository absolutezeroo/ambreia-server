using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Vortex.Rpg.Content.Authoring;

/// <summary>
/// A document's text as a file of the repository (content authoring §3): read as a JSON object,
/// written indented two spaces with its schema first and one newline at the end, the accents kept.
/// </summary>
public static class DocumentSource
{
    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The document, or null when the text is no JSON object.</summary>
    public static JsonObject? Parse(string text)
    {
        try
        {
            return JsonNode.Parse(text) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The document's id, or null when it has none.</summary>
    public static string? IdOf(string text) =>
        Parse(text)?["id"] is JsonValue id && id.GetValueKind() == JsonValueKind.String
            ? id.GetValue<string>()
            : null;

    public static string Format(string kind, JsonObject document)
    {
        JsonObject file = new() { ["$schema"] = $"../../schemas/content/{kind}.schema.json" };

        foreach ((string key, JsonNode? value) in document)
        {
            if (key != "$schema")
            {
                file[key] = value?.DeepClone();
            }
        }

        return file.ToJsonString(Indented) + "\n";
    }

    /// <summary>The same document whatever its layout and key order, its schema line aside.</summary>
    public static bool Same(JsonObject a, JsonObject b) => JsonNode.DeepEquals(Bare(a), Bare(b));

    private static JsonObject Bare(JsonObject document)
    {
        JsonObject bare = (JsonObject)document.DeepClone();

        bare.Remove("$schema");

        return bare;
    }
}
