using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Vortex.Primitives.Rpg.Items;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Content.Rules;
using Vortex.Rpg.Content.Validation;

namespace Vortex.Rpg.Quests.Rewards;

/// <summary>Items (spec M2a §3.3): one grant under the quest's key, so a retry gives nothing twice; overflow goes to parcels.</summary>
public sealed class ItemsReward : IQuestRewardKind
{
    public string Kind => "items";

    public async Task GrantAsync(QuestRewardDefinition reward, QuestGrant grant) =>
        await grant.Character.GrantItemsAsync(
            grant.GrantKey,
            [
                .. QuestFields
                    .Read<ItemsFields>(reward.Fields)
                    .Items.Select(l => new ItemGrantLine
                    {
                        DefinitionId = l.Item,
                        Quantity = l.Quantity,
                    }),
            ]
        );

    public IEnumerable<string> Problems(QuestRewardDefinition reward, ContentCheck check)
    {
        if (QuestFields.Problem<ItemsFields>(reward.Fields) is { } problem)
        {
            yield return problem;

            yield break;
        }

        foreach (ItemsLine line in QuestFields.Read<ItemsFields>(reward.Fields).Items)
        {
            if (!check.Is<ItemDefinition>(line.Item))
            {
                yield return $"no item '{line.Item}' in the content.";
            }

            if (line.Quantity < 1)
            {
                yield return $"{line.Item}: quantity is at least 1.";
            }
        }
    }
}
