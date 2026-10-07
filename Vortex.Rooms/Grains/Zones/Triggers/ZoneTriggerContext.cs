using System;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Object.Avatars;

namespace Vortex.Rooms.Grains.Zones.Triggers;

/// <summary>
/// What a condition reads and an action acts on: the room, its zone, the player who set it off
/// (none for a clock) and the server's local time.
/// </summary>
public sealed record ZoneTriggerContext(
    RoomGrain Room,
    ZoneRuntimeState Zone,
    PlayerId PlayerId,
    IRoomPlayer? Player,
    DateTimeOffset LocalNow
);
