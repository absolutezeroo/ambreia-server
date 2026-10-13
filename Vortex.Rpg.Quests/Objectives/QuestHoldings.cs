using System.Collections.Immutable;

namespace Vortex.Rpg.Quests.Objectives;

/// <summary>What a held objective reads: items by definition (bag and equipment) and the level.</summary>
public sealed record QuestHoldings(ImmutableDictionary<string, int> Items, int Level);
