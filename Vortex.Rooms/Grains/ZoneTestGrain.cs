using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.World.Grains;
using Vortex.Rooms.Configuration;

namespace Vortex.Rooms.Grains;

/// <summary>Pending test-mode sign-ins, by player (plan D18). In memory: a test is a few seconds.</summary>
[KeepAlive]
public sealed class ZoneTestGrain(
    IOptions<RoomConfig> config,
    IGrainFactory grainFactory,
    ILogger<ZoneTestGrain> logger
) : Grain, IZoneTestGrain
{
    private readonly Dictionary<
        PlayerId,
        (RoomId Room, int X, int Y, DateTime UntilUtc)
    > _pending = [];

    public Task ExpectAsync(PlayerId playerId, RoomId roomId, int x, int y)
    {
        DateTime now = DateTime.UtcNow;

        // A tester who never signed in leaves nothing behind for long.
        foreach (
            PlayerId stale in _pending
                .Where(p => p.Value.UntilUtc <= now)
                .Select(p => p.Key)
                .ToList()
        )
        {
            _pending.Remove(stale);
        }

        _pending[playerId] = (
            roomId,
            x,
            y,
            now.AddMilliseconds(config.Value.ZoneTestEntryTimeoutMs)
        );

        return Task.CompletedTask;
    }

    /// <remarks>
    /// The cell reaches the room here, at sign-in, rather than when the ticket was made: the room
    /// forgets an expected arrival after <c>ExpectedArrivalTimeoutMs</c>, and a client can take
    /// longer than that to load.
    /// </remarks>
    public async Task<RoomId?> TakeAsync(PlayerId playerId)
    {
        if (
            !_pending.Remove(playerId, out (RoomId Room, int X, int Y, DateTime UntilUtc) test)
            || test.UntilUtc <= DateTime.UtcNow
        )
        {
            return null;
        }

        if (test.X >= 0 && test.Y >= 0)
        {
            try
            {
                await grainFactory
                    .GetRoomZone(test.Room)
                    .ExpectArrivalAtAsync(playerId, test.X, test.Y);
            }
            catch (Exception ex)
            {
                // The tester still goes to the zone, on its arrival rather than the picked cell.
                logger.LogWarning(
                    ex,
                    "Test entry of player {PlayerId} could not reach room {RoomId}",
                    playerId.Value,
                    test.Room.Value
                );
            }
        }

        return test.Room;
    }
}
