using Vortex.Primitives.Players;

namespace Vortex.Primitives.Events.Rpg;

/// <summary>A server system took <paramref name="Quantity"/> of a copy (a quest, crafting; spec
/// Objets §9): one event per copy taken from.</summary>
public sealed record ItemRemovedEvent(
    PlayerId PlayerId,
    int ItemId,
    string DefinitionId,
    int Quantity
) : IEvent;
