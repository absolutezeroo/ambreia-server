using System;
using Vortex.Primitives.Players;

namespace Vortex.Primitives.Events;

/// <summary>Internal lifecycle event when a player session is detached from a game session.</summary>
public sealed record PlayerDisconnectedEvent(
    PlayerId PlayerId,
    DateTime ConnectedAtUtc,
    DateTime DisconnectedAtUtc,
    long SessionDurationSeconds
) : IEvent;
