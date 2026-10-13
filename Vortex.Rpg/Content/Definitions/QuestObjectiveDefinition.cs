using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vortex.Rpg.Content.Definitions;

/// <summary>
/// One objective of a quest (spec M2a §2.1): its kind, how many times, an optional text key, and the
/// kind's own fields, kept raw here and read strictly by the kind (<c>QuestFields.Read</c>).
/// </summary>
public sealed record QuestObjectiveDefinition
{
    public required string Kind { get; init; }

    public int Count { get; init; } = 1;

    /// <summary>The line's text key; by default <c>&lt;quest id&gt;.objective.&lt;index&gt;</c>.</summary>
    public string? Text { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Fields { get; init; } = [];
}
