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
/// Every other check the room does — ban, capacity, password, doorbell — has already passed.
/// </summary>
public sealed record PlayerEnteringRoomEvent(PlayerId PlayerId, int RoomId) : IEvent;

/// <summary>Player entered a room for a tracked user journey.</summary>
/// <param name="OwnerId">
/// Who owns the room. Carried so that "join their flat" is expressible without a follow event: a
/// reward-track step can match this against the player an earlier step captured.
/// </param>
public sealed record PlayerEnteredRoomEvent(
    PlayerId PlayerId,
    int RoomId,
    DateTime EnteredAtUtc,
    int OwnerId = 0
) : IEvent;

/// <summary>Player left a room for a tracked user journey.</summary>
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
