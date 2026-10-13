using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Events.Registry;
using Vortex.Primitives.Events.Rpg;
using Vortex.Primitives.Orleans;

namespace Vortex.Rpg.Quests.Signals;

/// <summary>An item was taken by a server system (a quest, crafting): held objectives are read again.</summary>
public sealed class ItemRemovedHoldings(IGrainFactory grains) : IEventHandler<ItemRemovedEvent>
{
    public async ValueTask HandleAsync(
        ItemRemovedEvent e,
        EventContext ctx,
        CancellationToken ct
    ) => await grains.GetQuestLogGrain(e.PlayerId).RefreshHeldAsync();
}
