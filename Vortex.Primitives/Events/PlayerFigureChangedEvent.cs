using Vortex.Primitives.Players;

namespace Vortex.Primitives.Events;

/// <summary>Player changed their avatar figure (look).</summary>
public sealed record PlayerFigureChangedEvent(PlayerId PlayerId, string Figure) : IEvent;
