using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Events.Registry;
using Vortex.Primitives.Events;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Rooms.Enums;

namespace Vortex.Progression.Achievements.Events;

/// <summary>
/// "For changing the wall / floor / landscape pattern of your room": one per decoration applied.
/// </summary>
/// <remarks>
/// A handler rather than a row in <see cref="AchievementSignalConsumer"/>: a signal action is a
/// reward-track artwork key, and the target client has none for a room decoration.
/// </remarks>
public sealed class RoomDecorationAchievementHandler(IGrainFactory grainFactory)
    : IEventHandler<RoomDecoratedEvent>
{
    public async ValueTask HandleAsync(RoomDecoratedEvent e, EventContext ctx, CancellationToken ct)
    {
        string? name = e.Property switch
        {
            RoomPropertyType.WALLPAPER => AchievementNames.RoomDecoWallpaper,
            RoomPropertyType.FLOOR => AchievementNames.RoomDecoFloor,
            RoomPropertyType.LANDSCAPE => AchievementNames.RoomDecoLandscape,
            _ => null,
        };

        if (name is null || e.PlayerId <= 0)
        {
            return;
        }

        await grainFactory
            .GetPlayerAchievementGrain(e.PlayerId)
            .ProgressAsync(name, 1, ct)
            .ConfigureAwait(false);
    }
}
