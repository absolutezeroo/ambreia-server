using Vortex.Primitives.Players;

namespace Vortex.Primitives.Events.Combat;

/// <summary>A player gave or took a hit while out of combat (spec M1a §6.3).</summary>
public sealed record CombatEnteredEvent(PlayerId Player) : IEvent;
