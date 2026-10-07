using System;
using System.Collections.Immutable;
using Vortex.Primitives.Players;

namespace Vortex.Primitives.Events;

/// <summary>Internal lifecycle event when a player session is attached to a game session.</summary>
public sealed record PlayerConnectedEvent(PlayerId PlayerId, DateTime ConnectedAtUtc) : IEvent;

/// <summary>Internal lifecycle event when a player session is detached from a game session.</summary>
public sealed record PlayerDisconnectedEvent(
    PlayerId PlayerId,
    DateTime ConnectedAtUtc,
    DateTime DisconnectedAtUtc,
    long SessionDurationSeconds
) : IEvent;

/// <summary>
/// Raised before a player is let into a room, and published cancellably: a behaviour that sets
/// <c>Cancel</c> refuses the entry, and the client is told the same way a locked door tells it.
/// Every other check the room does — capacity, locked door, password — has already passed.
/// </summary>
public sealed record PlayerEnteringRoomEvent(PlayerId PlayerId, int RoomId) : IEvent;

/// <summary>Player entered a room (room visit log and session audit).</summary>
/// <param name="OwnerId">Who owns the room. Nothing reads it today.</param>
public sealed record PlayerEnteredRoomEvent(
    PlayerId PlayerId,
    int RoomId,
    DateTime EnteredAtUtc,
    int OwnerId = 0
) : IEvent;

/// <summary>Player left a room (room visit log and session audit).</summary>
public sealed record PlayerLeftRoomEvent(
    PlayerId PlayerId,
    int RoomId,
    DateTime LeftAtUtc,
    long RoomDurationSeconds
) : IEvent;

/// <summary>Player changed their motto.</summary>
public sealed record PlayerMottoChangedEvent(PlayerId PlayerId, string Motto) : IEvent;

/// <summary>Player changed their avatar figure (look).</summary>
public sealed record PlayerFigureChangedEvent(PlayerId PlayerId, string Figure) : IEvent;
