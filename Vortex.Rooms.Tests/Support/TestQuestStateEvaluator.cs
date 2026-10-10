using System.Collections.Generic;
using Vortex.Primitives.World.Rules;
using Vortex.Rpg.Zones.Rules;

namespace Vortex.Rooms.Tests.Support;

/// <summary>The quest log's condition: the quest is in the state asked.</summary>
internal sealed class TestQuestStateEvaluator : ZoneConditionEvaluator<QuestStateCondition>
{
    protected override bool Holds(QuestStateCondition condition, ZoneRuleContext ctx) =>
        ctx.FactsOf<TestQuestFacts>()?.States.GetValueOrDefault(condition.Quest) == condition.State;
}
