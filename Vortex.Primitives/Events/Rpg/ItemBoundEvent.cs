using Vortex.Primitives.Players;

namespace Vortex.Primitives.Events.Rpg;

/// <summary>A copy became bound to its owner when equipped (spec Objets §9). A copy bound on
/// pickup says so in its <see cref="ItemGrantedEvent"/>.</summary>
public sealed record ItemBoundEvent(PlayerId PlayerId, int ItemId, string DefinitionId) : IEvent;
