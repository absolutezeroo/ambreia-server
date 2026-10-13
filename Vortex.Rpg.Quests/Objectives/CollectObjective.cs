using System.Collections.Generic;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Content.Rules;
using Vortex.Rpg.Content.Validation;

namespace Vortex.Rpg.Quests.Objectives;

/// <summary>Have N of an item, bag and equipment (spec M2a §2.1); held, so selling them takes the quest back.</summary>
public sealed class CollectObjective : IHeldObjectiveKind
{
    public string Kind => "collect";

    public int Current(QuestObjectiveDefinition objective, QuestHoldings holdings) =>
        holdings.Items.GetValueOrDefault(QuestFields.Read<CollectFields>(objective.Fields).Item);

    public IEnumerable<string> Problems(QuestObjectiveDefinition objective, ContentCheck check)
    {
        if (QuestFields.Problem<CollectFields>(objective.Fields) is { } problem)
        {
            yield return problem;

            yield break;
        }

        string item = QuestFields.Read<CollectFields>(objective.Fields).Item;

        if (!check.Is<ItemDefinition>(item))
        {
            yield return $"no item '{item}' in the content.";
        }
    }
}
