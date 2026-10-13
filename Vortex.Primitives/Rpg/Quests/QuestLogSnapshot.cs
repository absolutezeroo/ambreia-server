using System.Collections.Immutable;
using Orleans;

namespace Vortex.Primitives.Rpg.Quests;

/// <summary>The whole log, as RpgQuestLog sends it (spec M2a §3.2).</summary>
[GenerateSerializer, Immutable]
public sealed record QuestLogSnapshot([property: Id(0)] ImmutableArray<QuestEntrySnapshot> Entries);
