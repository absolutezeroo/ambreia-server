using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Events.Registry;
using Vortex.Primitives.Events.Rpg;
using Vortex.Primitives.Orleans;

namespace Vortex.Rpg.Quests.Signals;

/// <summary>An item came in: held objectives (collect) are read again.</summary>
public sealed class ItemGrantedHoldings(IGrainFactory grains) : IEventHandler<ItemGrantedEvent>
{
    public async ValueTask HandleAsync(
        ItemGrantedEvent e,
        EventContext ctx,
        CancellationToken ct
    ) => await grains.GetQuestLogGrain(e.PlayerId).RefreshHeldAsync();
}
