using Vortex.Primitives.Players;

namespace Vortex.Primitives.Events.Rpg;

/// <summary>An avatar became a character: it chose its class (spec Personnage §11).</summary>
public sealed record CharacterCreatedEvent(
    PlayerId PlayerId,
    string ClassId,
    string SpecializationId
) : IEvent;
