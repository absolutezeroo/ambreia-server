using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Vortex.Primitives.Rpg.Characters.Grains;
using Vortex.Primitives.Rpg.Items;
using Vortex.Primitives.Rpg.Items.Enums;
using Vortex.Rpg.Quests.Objectives;

namespace Vortex.Rpg.Quests.Grains;

/// <summary>What the character holds, for held objectives: bag and equipment by definition, and the level. Read only.</summary>
internal static class QuestHoldingsReader
{
    public static async Task<QuestHoldings> ReadAsync(IPlayerCharacterGrain character)
    {
        CharacterItemSnapshot[] items = await character.GetItemsAsync();
        int level = (await character.GetSummaryAsync())?.Level ?? 0;

        return new QuestHoldings(
            items
                .Where(i => i.Container is ItemContainer.Bag or ItemContainer.Equipment)
                .GroupBy(i => i.DefinitionId)
                .ToImmutableDictionary(g => g.Key, g => g.Sum(i => i.Quantity)),
            level
        );
    }
}
