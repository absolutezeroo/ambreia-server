namespace Vortex.Primitives.Events;

/// <summary>An authentication attempt failed (unknown or expired SSO ticket).</summary>
public sealed record PlayerLoginFailedEvent(string? IpHash = null) : IEvent;
