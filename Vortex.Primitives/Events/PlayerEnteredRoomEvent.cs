using System;
using Vortex.Primitives.Players;

namespace Vortex.Primitives.Events;

/// <summary>Player entered a room (room visit log and session audit).</summary>
/// <param name="OwnerId">Who owns the room. Nothing reads it today.</param>
public sealed record PlayerEnteredRoomEvent(
    PlayerId PlayerId,
    int RoomId,
    DateTime EnteredAtUtc,
    int OwnerId = 0
) : IEvent;
