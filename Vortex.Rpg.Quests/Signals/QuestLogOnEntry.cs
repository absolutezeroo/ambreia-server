using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Events.Registry;
using Vortex.Primitives.Events;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;

namespace Vortex.Rpg.Quests.Signals;

/// <summary>A room entered: the player's client gets the whole quest log, as the character's state is pushed then.</summary>
public sealed class QuestLogOnEntry(IGrainFactory grains) : IEventHandler<PlayerEnteredRoomEvent>
{
    public async ValueTask HandleAsync(
        PlayerEnteredRoomEvent e,
        EventContext ctx,
        CancellationToken ct
    ) => await grains.GetQuestLogGrain(e.PlayerId).PushLogAsync();
}
