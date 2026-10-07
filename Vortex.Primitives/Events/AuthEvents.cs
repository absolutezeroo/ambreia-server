using System.Collections.Immutable;
using Vortex.Primitives.Players;

namespace Vortex.Primitives.Events;

/// <summary>A player successfully authenticated (SSO ticket validated).</summary>
public sealed record PlayerLoggedInEvent(int PlayerId, string? IpHash = null) : IEvent;

/// <summary>An authentication attempt failed (unknown or expired SSO ticket).</summary>
public sealed record PlayerLoginFailedEvent(string? IpHash = null) : IEvent;

/// <summary>A player claimed a new name; the old one rides along so the audit trail says who they were.</summary>
public sealed record PlayerNameChangedEvent(PlayerId PlayerId, string OldName, string NewName)
    : IEvent;
