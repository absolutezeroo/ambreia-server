using Vortex.Primitives.Players;

namespace Vortex.Rpg.Combat.Systems;

/// <summary>A player's cast (spec M1b §10.1): at a room object (0: none) or a cell (−1: none).</summary>
public sealed record CastRequest(PlayerId Player, string SkillId, int TargetObjectId, int X, int Y);
