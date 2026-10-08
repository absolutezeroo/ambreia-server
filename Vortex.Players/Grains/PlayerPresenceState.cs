using System;
using Vortex.Primitives.Rooms;

namespace Vortex.Players.Grains;

public sealed class PlayerPresenceLiveState
{
    public RoomId ActiveRoomId { get; set; } = -1;

    public DateTime ActiveRoomSinceUtc { get; set; } = DateTime.UtcNow;
}
