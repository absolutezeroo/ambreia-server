namespace Vortex.Primitives.Events;

/// <summary>A player accepted an incoming friend request (a social-graph edge was created).</summary>
public sealed record FriendRequestAcceptedEvent(int ActorPlayerId, int TargetPlayerId) : IEvent;

/// <summary>A player removed a friend (a social-graph edge was deleted).</summary>
public sealed record FriendRemovedEvent(int ActorPlayerId, int TargetPlayerId) : IEvent;

/// <summary>A player blocked another user.</summary>
public sealed record UserBlockedEvent(int ActorPlayerId, int TargetPlayerId) : IEvent;

/// <summary>A player unblocked a previously blocked user.</summary>
public sealed record UserUnblockedEvent(int ActorPlayerId, int TargetPlayerId) : IEvent;

/// <summary>
/// A friend request was sent. Recorded because the request, not the friendship, is what a player
/// being pestered actually reports -- and a refused one leaves no friendship behind at all.
/// </summary>
public sealed record FriendRequestSentEvent(int ActorPlayerId, int TargetPlayerId) : IEvent;

/// <summary>
/// A friend request was turned down. <paramref name="RequesterPlayerId"/> is null for the client's
/// "decline all", which names nobody.
/// </summary>
public sealed record FriendRequestDeclinedEvent(int ActorPlayerId, int? RequesterPlayerId) : IEvent;
