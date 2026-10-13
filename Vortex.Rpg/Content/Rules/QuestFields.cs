using System.Collections.Generic;
using System.Text.Json;

namespace Vortex.Rpg.Content.Rules;

/// <summary>
/// A quest piece's own fields, read into the kind's record with the content's strict options: a
/// missing required field, an unknown one or a wrong type is a <see cref="JsonException"/>.
/// </summary>
public static class QuestFields
{
    public static T Read<T>(IReadOnlyDictionary<string, JsonElement> fields) =>
        JsonSerializer.Deserialize<T>(
            JsonSerializer.Serialize(fields, ContentJson.Options),
            ContentJson.Options
        ) ?? throw new JsonException("The fields are null.");

    /// <summary>Why the fields do not read, or null when they do.</summary>
    public static string? Problem<T>(IReadOnlyDictionary<string, JsonElement> fields)
    {
        try
        {
            Read<T>(fields);

            return null;
        }
        catch (JsonException ex)
        {
            return ex.Message;
        }
    }
}
