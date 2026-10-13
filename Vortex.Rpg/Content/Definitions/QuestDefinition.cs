using System.Collections.Generic;
using Vortex.Primitives.World.Rules;

namespace Vortex.Rpg.Content.Definitions;

/// <summary>
/// A quest (spec M2a §2.1): what it asks (objectives), what it needs before it is offered
/// (requires, the zone rules' conditions) and what it gives (rewards). Each piece names a kind a
/// module declares; this record names none.
/// </summary>
public sealed record QuestDefinition : ContentDefinition
{
    /// <summary>The advised level, shown in the journal; it does not block.</summary>
    public required int Level { get; init; }

    public List<ZoneCondition> Requires { get; init; } = [];

    public required List<QuestObjectiveDefinition> Objectives { get; init; }

    public List<QuestRewardDefinition> Rewards { get; init; } = [];
}
