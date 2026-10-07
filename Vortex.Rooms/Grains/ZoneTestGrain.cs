using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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
public sealed class ZoneTestGrain(IOptions<RoomConfig> config, IGrainFactory grainFactory)
    : Grain,
        IZoneTestGrain
{
    private readonly Dictionary<PlayerId, (RoomId Room, DateTime UntilUtc)> _pending = [];

    public async Task ExpectAsync(PlayerId playerId, RoomId roomId, int x, int y)
    {
        _pending[playerId] = (
            roomId,
            DateTime.UtcNow.AddMilliseconds(config.Value.ZoneTestEntryTimeoutMs)
        );

        if (x >= 0 && y >= 0)
        {
            await grainFactory.GetRoomZone(roomId).ExpectArrivalAtAsync(playerId, x, y);
        }
    }

    public Task<RoomId?> TakeAsync(PlayerId playerId) =>
        Task.FromResult<RoomId?>(
            _pending.Remove(playerId, out (RoomId Room, DateTime UntilUtc) test)
            && test.UntilUtc > DateTime.UtcNow
                ? test.Room
                : null
        );
}
