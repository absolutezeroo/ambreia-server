namespace Vortex.Rpg.Combat.Hits;

/// <summary>
/// A step of the hit chain (spec M1a §6.2, §13), declared by DI by the module that owns it: dodge,
/// damage, crit, armor and minimum ship with combat; shields, buffs and vulnerabilities come with M1b.
/// </summary>
public interface IHitStep
{
    /// <summary>Lower runs first; equal priorities run by full type name.</summary>
    public int Priority { get; }

    /// <summary>Changes the hit; false stops the chain (a miss).</summary>
    public bool Apply(HitContext hit);
}
