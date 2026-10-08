using Vortex.Primitives.Players;

namespace Vortex.Primitives.Events;

/// <summary>Player changed their motto.</summary>
public sealed record PlayerMottoChangedEvent(PlayerId PlayerId, string Motto) : IEvent;
