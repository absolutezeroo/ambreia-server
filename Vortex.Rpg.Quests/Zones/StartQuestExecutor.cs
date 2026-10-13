using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.World.Rules;
using Vortex.Rpg.Content;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Zones.Rules;

namespace Vortex.Rpg.Quests.Zones;

/// <summary>
/// StartQuest {quest} (spec M2a §3.3): the quest's requirements by the room's rules, then the log's
/// own checks. Nobody set it off (a clock): nothing to start.
/// </summary>
public sealed class StartQuestExecutor(IGrainFactory grains, IRpgContent content)
    : ZoneActionExecutor<StartQuestAction>
{
    protected override async Task RunAsync(
        StartQuestAction action,
        ZoneRuleContext ctx,
        CancellationToken ct
    )
    {
        if (
            ctx.PlayerId.Value <= 0
            || content.Get<QuestDefinition>(action.Quest) is not { } quest
            || !ctx.AllHold(quest.Requires)
        )
        {
            return;
        }

        await grains.GetQuestLogGrain(ctx.PlayerId).AcceptAsync(action.Quest);
    }
}
