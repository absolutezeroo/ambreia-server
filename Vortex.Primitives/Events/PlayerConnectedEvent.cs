using System;
using Vortex.Primitives.Players;

namespace Vortex.Primitives.Events;

/// <summary>Internal lifecycle event when a player session is attached to a game session.</summary>
public sealed record PlayerConnectedEvent(PlayerId PlayerId, DateTime ConnectedAtUtc) : IEvent;
