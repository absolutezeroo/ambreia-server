using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vortex.Rpg.Content.Definitions;

/// <summary>One reward of a quest (spec M2a §2.1): its kind and the kind's own fields, read by the kind.</summary>
public sealed record QuestRewardDefinition
{
    public required string Kind { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Fields { get; init; } = [];
}
