namespace Vortex.Primitives.Events;

/// <summary>A player successfully authenticated (SSO ticket validated).</summary>
public sealed record PlayerLoggedInEvent(int PlayerId, string? IpHash = null) : IEvent;
