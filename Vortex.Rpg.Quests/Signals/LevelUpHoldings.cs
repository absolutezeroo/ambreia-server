using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Events.Registry;
using Vortex.Primitives.Events.Rpg;
using Vortex.Primitives.Orleans;

namespace Vortex.Rpg.Quests.Signals;

/// <summary>A level was gained: a level objective may be met.</summary>
public sealed class LevelUpHoldings(IGrainFactory grains) : IEventHandler<LevelUpEvent>
{
    public async ValueTask HandleAsync(LevelUpEvent e, EventContext ctx, CancellationToken ct) =>
        await grains.GetQuestLogGrain(e.PlayerId).RefreshHeldAsync();
}
