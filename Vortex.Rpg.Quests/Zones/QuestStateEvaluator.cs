using System.Collections.Generic;
using Vortex.Primitives.Rpg.Quests;
using Vortex.Primitives.World.Rules;
using Vortex.Rpg.Zones.Rules;

namespace Vortex.Rpg.Quests.Zones;

/// <summary>QuestState {quest, state} (spec M2a §2.2): none, active, ready or done; a quest not in the log is none.</summary>
public sealed class QuestStateEvaluator : ZoneConditionEvaluator<QuestStateCondition>
{
    protected override bool Holds(QuestStateCondition condition, ZoneRuleContext ctx) =>
        (
            ctx.FactsOf<QuestZoneFacts>()?.States.GetValueOrDefault(condition.Quest)
            ?? QuestCodes.Name(QuestStatus.None)
        ) == condition.State;
}
