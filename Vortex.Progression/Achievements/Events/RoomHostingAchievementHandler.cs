using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Events.Registry;
using Vortex.Primitives.Events;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Orleans.Snapshots.Room;

namespace Vortex.Progression.Achievements.Events;

/// <summary>
/// "For having visitors spend %limit% minutes in your rooms": a visitor's whole minutes, credited to
/// the room's owner when the visitor leaves.
/// </summary>
/// <remarks>
/// Not a signal-table row: that table credits the signal's player, and this credits somebody else.
/// A stay under a minute is dropped before the room is asked anything, so the common case — walking
/// through — costs nothing.
/// </remarks>
public sealed class RoomHostingAchievementHandler(IGrainFactory grainFactory)
    : IEventHandler<PlayerLeftRoomEvent>
{
    public async ValueTask HandleAsync(
        PlayerLeftRoomEvent e,
        EventContext ctx,
        CancellationToken ct
    )
    {
        int minutes = (int)(e.RoomDurationSeconds / 60);

        if (minutes <= 0 || e.RoomId <= 0)
        {
            return;
        }

        RoomSummarySnapshot room = await grainFactory
            .GetRoomCore(e.RoomId)
            .GetSummaryAsync()
            .ConfigureAwait(false);

        if (room.OwnerId.Value <= 0 || room.OwnerId.Value == e.PlayerId.Value)
        {
            return;
        }

        await grainFactory
            .GetPlayerAchievementGrain(room.OwnerId.Value)
            .ProgressAsync(AchievementNames.RoomDecoHosting, minutes, ct)
            .ConfigureAwait(false);
    }
}
