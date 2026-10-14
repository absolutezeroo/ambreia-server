using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;

namespace Vortex.Rpg.Content;

/// <summary>
/// A kind's JSON schema, exported from its record (spec Contenu §4.2): what schemas/content/ commits
/// for an editor's autocompletion, and what the Game Editor builds a document's form from (content
/// authoring, Objets screen), so a field added to a record shows in both without more code.
/// </summary>
public static class ContentSchemas
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static string For(Type definition) =>
        JsonSchemaExporter
            .GetJsonSchemaAsNode(
                ContentJson.Options,
                definition,
                new JsonSchemaExporterOptions
                {
                    TreatNullObliviousAsNonNullable = true,
                    TransformSchemaNode = Transform,
                }
            )
            .ToJsonString(Indented)
            .ReplaceLineEndings("\n");

    private static JsonNode Transform(JsonSchemaExporterContext ctx, JsonNode schema)
    {
        // StrictStringListConverter is custom: the exporter only sees "anything" without this.
        if (ctx.TypeInfo.Type == typeof(List<string>))
        {
            return new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject { ["type"] = "string" },
            };
        }

        // A record with extension data (a quest piece) keeps its kind's fields: open, not refused.
        if (ctx.TypeInfo.Properties.Any(p => p.IsExtensionData) && schema is JsonObject open)
        {
            open.Remove("additionalProperties");
        }

        // The root admits the editor's "$schema" line, which ContentParser removes before reading.
        if (ctx.Path.IsEmpty && schema["properties"] is JsonObject properties)
        {
            properties.Insert(0, "$schema", new JsonObject { ["type"] = "string" });
        }

        return schema;
    }
}
