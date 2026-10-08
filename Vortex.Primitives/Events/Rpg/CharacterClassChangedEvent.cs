using Vortex.Primitives.Players;

namespace Vortex.Primitives.Events.Rpg;

/// <summary>A character left the starter class for its real class, once; level and XP kept.</summary>
public sealed record CharacterClassChangedEvent(
    PlayerId PlayerId,
    string OldClassId,
    string NewClassId
) : IEvent;
