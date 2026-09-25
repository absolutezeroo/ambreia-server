using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Events.Registry;
using Vortex.Primitives.Events;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players.Grains;

namespace Vortex.Progression.Achievements.Events;

/// <summary>
/// The two room-builder achievements that measure the room rather than count placements: "a room
/// with %limit% different furni" and "%limit% floor hole furni".
/// </summary>
/// <remarks>
/// Both raise the bar to what the room now holds instead of adding one, so picking an item up and
/// putting it back advances nothing — the hole count is how the reference emulator words it too.
/// </remarks>
public sealed class ItemPlacedAchievementHandler(IGrainFactory grainFactory)
    : IEventHandler<ItemPlacedEvent>
{
    /// <summary>The client's logic key for hole1x1, hole2, hole3, hole4 (RoomObjectLogicEnum).</summary>
    internal const string FLOOR_HOLE_LOGIC = "furniture_floor_hole";

    public async ValueTask HandleAsync(ItemPlacedEvent e, EventContext ctx, CancellationToken ct)
    {
        if (e.ActorPlayerId <= 0 || e.KindsInRoom <= 0)
        {
            return;
        }

        IPlayerAchievementGrain grain = grainFactory.GetPlayerAchievementGrain(e.ActorPlayerId);

        await grain
            .ProgressToAsync(AchievementNames.RoomDecoFurniTypeCount, e.KindsInRoom, ct)
            .ConfigureAwait(false);

        if (e.LogicName == FLOOR_HOLE_LOGIC)
        {
            await grain
                .ProgressToAsync(AchievementNames.RoomDecoHoleFurniCount, e.SameLogicInRoom, ct)
                .ConfigureAwait(false);
        }
    }
}
