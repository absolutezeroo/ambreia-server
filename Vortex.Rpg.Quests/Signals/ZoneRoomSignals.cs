using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Events;
using Vortex.Primitives.Rooms.Events.Zone;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Primitives.Rpg.Quests;

namespace Vortex.Rpg.Quests.Signals;

/// <summary>
/// The room's own happenings quests count (spec M2a §3.4): an area entered, a decor object used.
/// One per room, made by the room event listener extension point.
/// </summary>
[RoomEventListener]
public sealed class ZoneRoomSignals(IRoomGrain room, IGrainFactory grains, ZoneKeys keys)
    : IRoomEventListener
{
    public Task OnRoomEventAsync(RoomEvent evt, CancellationToken ct) =>
        evt switch
        {
            ZoneAreaEnteredEvent e => SignalAsync(
                e.RoomId,
                e.PlayerId,
                QuestSignals.AreaEntered,
                "area",
                e.AreaId
            ),
            ZoneDecorUsedEvent e => SignalAsync(
                e.RoomId,
                e.PlayerId,
                QuestSignals.DecorUsed,
                "object",
                e.DecorId
            ),
            _ => Task.CompletedTask,
        };

    private async Task SignalAsync(
        RoomId roomId,
        PlayerId player,
        string name,
        string subject,
        string value
    )
    {
        if (player.Value <= 0 || await keys.KeyOfAsync(roomId) is not { } zone)
        {
            return;
        }

        await grains
            .GetQuestLogGrain(player)
            .CountAsync(
                new QuestSignal(
                    name,
                    zone,
                    ImmutableDictionary<string, string>.Empty.Add(subject, value)
                )
            );
    }
}
