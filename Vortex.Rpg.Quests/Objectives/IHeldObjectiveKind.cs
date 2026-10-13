using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Content.Rules;

namespace Vortex.Rpg.Quests.Objectives;

/// <summary>An objective read from what the player holds now (spec M2a §2.1): it can go down.</summary>
public interface IHeldObjectiveKind : IQuestObjectiveRule
{
    public int Current(QuestObjectiveDefinition objective, QuestHoldings holdings);
}
