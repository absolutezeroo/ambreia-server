using System;
using Vortex.Primitives.Players;

namespace Vortex.Primitives.Events;

/// <summary>Player left a room (room visit log and session audit).</summary>
public sealed record PlayerLeftRoomEvent(
    PlayerId PlayerId,
    int RoomId,
    DateTime LeftAtUtc,
    long RoomDurationSeconds
) : IEvent;
