using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Object.Avatars;
using Vortex.Primitives.World.Layers;
using Vortex.Protocol.Messages.Outgoing.Room.Session;

namespace Vortex.Rooms.Grains.Zones;

/// <summary>
/// Passages (plan D8, D9): a step onto one takes the player to its destination entry -- in this
/// zone on the spot, in another through RoomForward once the destination expects them.
/// </summary>
public sealed class ZonePassageModule(RoomGrain roomGrain)
{
    /// <summary>Whether the step onto (x, y) took a passage; the walk is over if so.</summary>
    public async Task<bool> TryTakeAsync(IRoomPlayer player, int x, int y, CancellationToken ct)
    {
        ZoneRuntimeState? zone = roomGrain._state.ZoneRuntime;

        // Not a visitor yet: this is the arrival step, and an entry on a passage must not bounce
        // the player straight back.
        if (
            zone?.Compiled.PassageAt(x, y) is not { } passage
            || !zone.Visitors.TryGetValue(player.PlayerId, out ZoneVisitor? visitor)
        )
        {
            return false;
        }

        long now = roomGrain.NowMs();

        if (visitor.PassageReadyAtMs.GetValueOrDefault(passage.Id) > now)
        {
            return false;
        }

        // ponytail: conditions come with their evaluators (phase 5); until then a conditional
        // passage stays closed rather than open to everyone. Cost is RPG currency: ignored.
        if (passage.Conditions.Count > 0)
        {
            return false;
        }

        visitor.PassageReadyAtMs[passage.Id] = now + passage.CooldownSeconds * 1000L;

        roomGrain.AvatarModule.CancelWalk(player);

        if (passage.DestinationZone == zone.Compiled.Document.Settings.Id)
        {
            await MoveWithinAsync(player, passage, ct);

            return true;
        }

        RoomId? destination = await roomGrain._zoneSource.FindPublishedRoomAsync(
            passage.DestinationZone,
            ct
        );

        if (destination is not { } room)
        {
            ZoneBubble.Show(
                roomGrain,
                player.PlayerId,
                "zone.passage",
                "Ce passage ne mène nulle part pour l'instant."
            );

            return true;
        }

        await roomGrain
            ._grainFactory.GetRoomZone(room)
            .ExpectArrivalAsync(player.PlayerId, passage.DestinationEntry);
        await roomGrain
            ._grainFactory.GetPlayerPresenceGrain(player.PlayerId)
            .SendComposerAsync(new RoomForwardMessageComposer { RoomId = room });

        return true;
    }

    private async Task MoveWithinAsync(
        IRoomPlayer player,
        ZonePassage passage,
        CancellationToken ct
    )
    {
        ZoneRuntimeState zone = roomGrain._state.ZoneRuntime!;

        if (
            !zone.Compiled.Entries.TryGetValue(passage.DestinationEntry, out ZoneEntry? entry)
            || !roomGrain.MapModule.InBounds(entry.Cell.X, entry.Cell.Y)
        )
        {
            return;
        }

        roomGrain.AvatarModule.Teleport(player, entry.Cell.X, entry.Cell.Y);

        await roomGrain.ZoneAreas.UpdateAsync(player.PlayerId, entry.Cell.X, entry.Cell.Y, ct);
    }
}
