using Vortex.Primitives.Players;

namespace Vortex.Primitives.Events.Rpg;

/// <summary>The player destroyed <paramref name="Quantity"/> of a copy in the bag (spec Objets §9).</summary>
public sealed record ItemDestroyedEvent(
    PlayerId PlayerId,
    int ItemId,
    string DefinitionId,
    int Quantity
) : IEvent;
