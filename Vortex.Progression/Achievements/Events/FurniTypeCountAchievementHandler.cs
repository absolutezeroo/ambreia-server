using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Events.Registry;
using Vortex.Primitives.Events;
using Vortex.Primitives.Orleans;

namespace Vortex.Progression.Achievements.Events;

/// <summary>
/// "For building a room with %limit% different furni": one per placement of a definition the room
/// did not already hold.
/// </summary>
/// <remarks>
/// ponytail: cumulative like RoomDecoFurniCount next door, so picking an item up and putting it back
/// counts again. Recounting the room's distinct definitions and setting the bar needs a
/// set-not-add progress call the grain does not have.
/// </remarks>
public sealed class FurniTypeCountAchievementHandler(IGrainFactory grainFactory)
    : IEventHandler<ItemPlacedEvent>
{
    public async ValueTask HandleAsync(ItemPlacedEvent e, EventContext ctx, CancellationToken ct)
    {
        if (!e.FirstOfItsKindInRoom || e.ActorPlayerId <= 0)
        {
            return;
        }

        await grainFactory
            .GetPlayerAchievementGrain(e.ActorPlayerId)
            .ProgressAsync(AchievementNames.RoomDecoFurniTypeCount, 1, ct)
            .ConfigureAwait(false);
    }
}
