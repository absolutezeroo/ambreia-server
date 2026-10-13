namespace Vortex.Rpg.Quests.Objectives;

/// <summary>collect's fields: the item, and whether the hand-in takes them.</summary>
public sealed record CollectFields(string Item, bool Consume = false);
