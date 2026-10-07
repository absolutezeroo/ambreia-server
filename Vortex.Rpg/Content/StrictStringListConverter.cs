using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vortex.Rpg.Content;

/// <summary>
/// A list of text in a content document holds text only: <c>RespectNullableAnnotations</c> does not
/// look inside collections, and a null id would later reach a dictionary lookup.
/// </summary>
public sealed class StrictStringListConverter : JsonConverter<List<string>>
{
    public override List<string> Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException("A list is expected.");
        }

        List<string> values = [];

        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            values.Add(
                reader.TokenType == JsonTokenType.String
                    ? reader.GetString()!
                    : throw new JsonException("A list holds text only, never null or a number.")
            );
        }

        return values;
    }

    public override void Write(
        Utf8JsonWriter writer,
        List<string> value,
        JsonSerializerOptions options
    )
    {
        writer.WriteStartArray();

        foreach (string v in value)
        {
            writer.WriteStringValue(v);
        }

        writer.WriteEndArray();
    }
}
