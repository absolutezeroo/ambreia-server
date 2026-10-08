using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Events.Registry;
using Vortex.Primitives.Events;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Rooms;

namespace Vortex.Rpg.Characters.Events;

/// <summary>Every room entered is offered to the character as its last zone (spec Personnage §9);
/// the grain keeps world zones only.</summary>
public sealed class LastZoneHandler(IGrainFactory grainFactory)
    : IEventHandler<PlayerEnteredRoomEvent>
{
    public async ValueTask HandleAsync(
        PlayerEnteredRoomEvent e,
        EventContext ctx,
        CancellationToken ct
    ) =>
        await grainFactory
            .GetPlayerCharacterGrain(e.PlayerId)
            .EnteredRoomAsync(new RoomId(e.RoomId));
}
