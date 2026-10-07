using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Logging.Extensions;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;
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

        return zone?.Compiled.PassageAt(x, y) is { } passage
            && await TryUseAsync(player, passage, ct);
    }

    /// <summary>
    /// Takes a passage if its rules let this player through now: a step onto it, or a teleporter
    /// that names it. False leaves the player where they are.
    /// </summary>
    public async Task<bool> TryUseAsync(
        IRoomPlayer player,
        ZonePassage passage,
        CancellationToken ct
    )
    {
        // Not a visitor yet: this is the arrival step, and an entry on a passage must not bounce
        // the player straight back.
        if (
            roomGrain._state.ZoneRuntime is not { } zone
            || !zone.Visitors.ContainsKey(player.PlayerId)
        )
        {
            return false;
        }

        long now = roomGrain.NowMs();
        string cooldown = $"passage:{passage.Id}";

        if (zone.Cooldowns.IsCooling(player.PlayerId, cooldown, now))
        {
            return false;
        }

        // Cost is RPG currency: ignored until there is one.
        if (!roomGrain.ZoneTriggers.ConditionsHold(passage.Conditions, player.PlayerId))
        {
            return false;
        }

        if (passage.CooldownSeconds > 0)
        {
            zone.Cooldowns.Start(
                player.PlayerId,
                cooldown,
                now + passage.CooldownSeconds * 1000L,
                now
            );
        }

        await TakeAsync(player, passage, ct);

        return true;
    }

    /// <summary>Sends the player through a passage, wherever they stand (a step, a teleporter).</summary>
    public async Task TakeAsync(IRoomPlayer player, ZonePassage passage, CancellationToken ct)
    {
        CompiledZone zone = roomGrain._state.ZoneRuntime!.Compiled;

        roomGrain.AvatarModule.CancelWalk(player);

        if (passage.DestinationZone == zone.Document.Settings.Id)
        {
            if (zone.Entries.TryGetValue(passage.DestinationEntry, out ZoneEntry? entry))
            {
                await MoveToAsync(player, entry, ct);
            }

            return;
        }

        // Detached: the lookup reads the database and the destination may have to activate first,
        // and the room's tick -- every other avatar's step -- must not wait for either. Nothing
        // below touches this room's state.
        HandOffAsync(player.PlayerId, passage, ct)
            .LogAndForget(
                roomGrain._logger,
                "Passage hand-off of player {PlayerId} to zone {Zone} failed",
                player.PlayerId.Value,
                passage.DestinationZone
            );
    }

    private async Task HandOffAsync(PlayerId playerId, ZonePassage passage, CancellationToken ct)
    {
        RoomId? destination = await roomGrain._zoneSource.FindPublishedRoomAsync(
            passage.DestinationZone,
            ct
        );

        if (destination is not { } room)
        {
            ZoneBubble.Show(
                roomGrain,
                playerId,
                "zone.passage",
                "Ce passage ne mène nulle part pour l'instant."
            );

            return;
        }

        if (
            !await roomGrain
                ._grainFactory.GetRoomZone(room)
                .ExpectArrivalAsync(playerId, passage.DestinationEntry)
        )
        {
            ZoneBubble.Show(roomGrain, playerId, "zone.passage", "Cette zone est pleine.");

            return;
        }

        await roomGrain
            ._grainFactory.GetPlayerPresenceGrain(playerId)
            .SendComposerAsync(new RoomForwardMessageComposer { RoomId = room });
    }

    /// <summary>Puts the player on an entry of this zone at once, and updates their areas.</summary>
    public async Task MoveToAsync(IRoomPlayer player, ZoneEntry entry, CancellationToken ct)
    {
        // Never onto a cell nobody can stand on: the player would be stuck there.
        if (
            !roomGrain.MapModule.InBounds(entry.Cell.X, entry.Cell.Y)
            || !roomGrain.MapModule.IsSpawnable(
                roomGrain.MapModule.ToIdx(entry.Cell.X, entry.Cell.Y)
            )
        )
        {
            return;
        }

        roomGrain.AvatarModule.Teleport(player, entry.Cell.X, entry.Cell.Y);

        await roomGrain.ZoneAreas.UpdateAsync(player.PlayerId, entry.Cell.X, entry.Cell.Y, ct);
    }
}
