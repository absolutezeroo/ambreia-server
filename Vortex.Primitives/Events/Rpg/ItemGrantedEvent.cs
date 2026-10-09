using Vortex.Primitives.Players;

namespace Vortex.Primitives.Events.Rpg;

/// <summary>A grant put <paramref name="Quantity"/> of an item into one copy (spec Objets §9):
/// one event per copy the grant touched.</summary>
public sealed record ItemGrantedEvent(
    PlayerId PlayerId,
    string GrantKey,
    int ItemId,
    string DefinitionId,
    int Quantity,
    bool Bound
) : IEvent;
