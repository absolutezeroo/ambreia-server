using System.Collections.Immutable;
using Orleans;
using Vortex.Primitives.World.Rules;

namespace Vortex.Primitives.Rpg.Quests;

/// <summary>
/// The quest log's part of what a zone knows of a player (spec M2a §3.3, Z1 §4.3): each quest in the
/// log by its status name (active, ready, done); a quest not there is "none". Pushed on each change.
/// </summary>
[GenerateSerializer, Immutable]
public sealed record QuestZoneFacts : ZoneFacts
{
    [Id(0)]
    public required ImmutableDictionary<string, string> States { get; init; }
}
