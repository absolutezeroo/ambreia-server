using Vortex.Primitives.Players;

namespace Vortex.Primitives.Events.Combat;

/// <summary>A player is out of combat (spec M1a §6.3): no hit for the timeout, no monster after him.</summary>
public sealed record CombatLeftEvent(PlayerId Player) : IEvent;
