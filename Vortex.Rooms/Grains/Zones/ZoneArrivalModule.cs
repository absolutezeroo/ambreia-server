using System.Collections.Generic;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.World.Layers;

namespace Vortex.Rooms.Grains.Zones;

/// <summary>
/// Where a player lands in a zone (plan D3): the entry they are expected on, else the zone's first
/// "arrival", else the model's door — an entry they could not stand on is skipped.
/// </summary>
public sealed class ZoneArrivalModule(RoomGrain roomGrain)
{
    public void Expect(PlayerId playerId, string entryId)
    {
        ZoneRuntimeState? zone = roomGrain._state.ZoneRuntime;

        if (zone is null || !zone.Compiled.Entries.ContainsKey(entryId))
        {
            return;
        }

        zone.ExpectedArrivals[playerId] = (
            entryId,
            roomGrain.NowMs() + roomGrain._roomConfig.ExpectedArrivalTimeoutMs
        );
    }

    /// <summary>The cell and facing of the player's arrival, or null for the model's door.</summary>
    public (int X, int Y, Rotation Rotation)? TakeArrival(PlayerId playerId)
    {
        ZoneRuntimeState? zone = roomGrain._state.ZoneRuntime;

        if (zone is null)
        {
            return null;
        }

        ZoneEntry? entry = null;

        if (zone.ExpectedArrivals.Remove(playerId, out (string EntryId, long ExpiresAtMs) expected))
        {
            if (expected.ExpiresAtMs >= roomGrain.NowMs())
            {
                entry = Standable(zone.Compiled.Entries.GetValueOrDefault(expected.EntryId));
            }
        }

        entry ??= Standable(zone.Compiled.DefaultArrival);

        return entry is null ? null : (entry.Cell.X, entry.Cell.Y, (Rotation)(entry.Direction & 7));
    }

    private ZoneEntry? Standable(ZoneEntry? entry) =>
        entry is not null
        && roomGrain.MapModule.InBounds(entry.Cell.X, entry.Cell.Y)
        && roomGrain.MapModule.IsSpawnable(roomGrain.MapModule.ToIdx(entry.Cell.X, entry.Cell.Y))
            ? entry
            : null;
}
