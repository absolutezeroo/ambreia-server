using Vortex.Primitives.Rpg.Characters.Grains;

namespace Vortex.Rpg.Quests.Rewards;

/// <summary>Who receives, for which quest, and the key that makes an items grant happen once.</summary>
public sealed record QuestGrant(IPlayerCharacterGrain Character, string QuestId, string GrantKey);
