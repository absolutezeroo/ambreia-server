namespace Vortex.Primitives.Events.Rpg;

/// <summary>The content registry now serves this pack version (spec Contenu §10); the counts are
/// against what it served before.</summary>
public sealed record ContentReloadedEvent(long PackVersion, int Added, int Changed, int Removed)
    : IEvent;
