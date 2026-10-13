using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Events.Registry;
using Vortex.Primitives.Events.Rpg;
using Vortex.Primitives.Orleans;

namespace Vortex.Rpg.Quests.Signals;

/// <summary>An item was destroyed by its owner: held objectives are read again.</summary>
public sealed class ItemDestroyedHoldings(IGrainFactory grains) : IEventHandler<ItemDestroyedEvent>
{
    public async ValueTask HandleAsync(
        ItemDestroyedEvent e,
        EventContext ctx,
        CancellationToken ct
    ) => await grains.GetQuestLogGrain(e.PlayerId).RefreshHeldAsync();
}
