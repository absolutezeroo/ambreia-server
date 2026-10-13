using Vortex.Primitives.Rpg.Quests;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Content.Rules;

namespace Vortex.Rpg.Quests.Objectives;

/// <summary>An objective counted from signals (spec M2a §2.1): the counter only goes up.</summary>
public interface ICountedObjectiveKind : IQuestObjectiveRule
{
    /// <summary>What this signal adds to this objective; 0 when it does not match.</summary>
    public int Counts(QuestObjectiveDefinition objective, QuestSignal signal);
}
