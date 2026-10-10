namespace Vortex.Rpg.Combat.Systems;

/// <summary>A cast (spec M1b §10.1, M1c §4.2) by a fighter of the zone — a player or a monster — at a
/// room object (0: none) or a cell (−1: none).</summary>
public sealed record CastRequest(
    int CasterObjectId,
    string SkillId,
    int TargetObjectId,
    int X,
    int Y
);
