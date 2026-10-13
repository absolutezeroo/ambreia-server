using System.Collections.Immutable;
using Orleans;

namespace Vortex.Primitives.Rpg.Quests;

/// <summary>
/// Something a player did that a quest may count (spec M2a §3.4): a name (<see cref="QuestSignals"/>),
/// the zone key it happened in, what it was about (monster, area, object, npc…) and how many.
/// A module that adds a signal sends it to the player's quest log; no quest code changes.
/// </summary>
[GenerateSerializer, Immutable]
public sealed record QuestSignal(
    [property: Id(0)] string Name,
    [property: Id(1)] string Zone,
    [property: Id(2)] ImmutableDictionary<string, string> Subject,
    [property: Id(3)] int Amount = 1
);
