using Vortex.Primitives.Players;

namespace Vortex.Primitives.Events;

/// <summary>A player claimed a new name; the old one rides along so the audit trail says who they were.</summary>
public sealed record PlayerNameChangedEvent(PlayerId PlayerId, string OldName, string NewName)
    : IEvent;
