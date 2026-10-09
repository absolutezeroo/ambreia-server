namespace Vortex.Rpg.Combat.Hits;

/// <summary>How a hit departs from an automatic swing (spec M1b §5.2, §7.2): a skill's own amount,
/// no dodge roll, no crit roll.</summary>
public sealed record HitOptions
{
    public static readonly HitOptions None = new();

    /// <summary>Set: replaces the rolled base damage.</summary>
    public double? BaseDamage { get; init; }

    public bool Unavoidable { get; init; }

    public bool NoCrit { get; init; }
}
