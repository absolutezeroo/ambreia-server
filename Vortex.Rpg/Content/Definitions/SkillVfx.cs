namespace Vortex.Rpg.Content.Definitions;

/// <summary>
/// What the client draws for a skill or a basic attack, by moment: the kits of a spell's visual as
/// MMORPGs cut them (precast, cast, missile, area, impact), each one optional. The server never
/// reads them; they go to the client with the public definitions.
/// </summary>
public sealed record SkillVfx
{
    /// <summary>Looped on the caster from the cast's start to its end (a channel's too).</summary>
    public VfxPhase? Cast { get; init; }

    /// <summary>Played once on the caster the moment the skill goes off.</summary>
    public VfxPhase? Release { get; init; }

    /// <summary>Flies from the caster to the target when it goes off; the area, the hit and the numbers wait for it.</summary>
    public VfxProjectile? Projectile { get; init; }

    /// <summary>Played once where it landed: under the target, or on the aimed cell.</summary>
    public VfxPhase? Area { get; init; }

    /// <summary>Played once on each one it touched (a miss shows nothing).</summary>
    public VfxPhase? Hit { get; init; }
}
