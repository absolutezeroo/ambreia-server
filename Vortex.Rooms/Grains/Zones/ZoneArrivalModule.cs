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

        zone.ExpectedArrivals[playerId] = new(entryId, -1, -1, ExpiresAt());
    }

    public void ExpectAt(PlayerId playerId, int x, int y)
    {
        if (roomGrain._state.ZoneRuntime is { } zone && x >= 0 && y >= 0)
        {
            zone.ExpectedArrivals[playerId] = new(null, x, y, ExpiresAt());
        }
    }

    /// <summary>The cell and facing of the player's arrival, or null for the model's door.</summary>
    public (int X, int Y, Rotation Rotation)? TakeArrival(PlayerId playerId)
    {
        ZoneRuntimeState? zone = roomGrain._state.ZoneRuntime;

        if (zone is null)
        {
            return null;
        }

        if (
            zone.ExpectedArrivals.Remove(playerId, out ZoneExpectedArrival expected)
            && expected.ExpiresAtMs >= roomGrain.NowMs()
        )
        {
            if (expected.EntryId is null && Standable(expected.X, expected.Y))
            {
                return (expected.X, expected.Y, Rotation.East);
            }

            if (
                Standable(
                    zone.Compiled.Entries.GetValueOrDefault(expected.EntryId ?? string.Empty)
                ) is
                { } chosen
            )
            {
                return At(chosen);
            }
        }

        return Standable(zone.Compiled.DefaultArrival) is { } arrival ? At(arrival) : null;
    }

    private long ExpiresAt() => roomGrain.NowMs() + roomGrain._roomConfig.ExpectedArrivalTimeoutMs;

    private static (int X, int Y, Rotation Rotation) At(ZoneEntry entry) =>
        (entry.Cell.X, entry.Cell.Y, (Rotation)(entry.Direction & 7));

    private ZoneEntry? Standable(ZoneEntry? entry) =>
        entry is not null && Standable(entry.Cell.X, entry.Cell.Y) ? entry : null;

    private bool Standable(int x, int y) =>
        roomGrain.MapModule.InBounds(x, y)
        && roomGrain.MapModule.IsSpawnable(roomGrain.MapModule.ToIdx(x, y));
}
