using System.Collections.Immutable;
using Orleans;

namespace Vortex.Primitives.Rpg.Quests;

[GenerateSerializer, Immutable]
public sealed record QuestEntrySnapshot(
    [property: Id(0)] string QuestId,
    [property: Id(1)] QuestStatus Status,
    [property: Id(2)] ImmutableArray<int> Progress
);
