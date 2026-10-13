namespace Vortex.Rpg.Content.Definitions;

/// <summary>
/// One effect of a skill (spec M1b §5.2). Flat on purpose: <see cref="Kind"/> names an effect type a
/// module declares, and that type says which of the other fields it needs.
/// </summary>
public sealed record SkillEffectDefinition
{
    public required string Kind { get; init; }

    /// <summary>Amount before the caster's power.</summary>
    public double Base { get; init; }

    /// <summary>Times the caster's power.</summary>
    public double Coefficient { get; init; }

    /// <summary>A status id.</summary>
    public string? Status { get; init; }

    /// <summary>A count (statuses to dispel).</summary>
    public int Amount { get; init; }

    /// <summary>Extra threat, as a percent of this skill's damage on the target.</summary>
    public int ThreatPercent { get; init; }

    /// <summary>Vie et mana rendues par une résurrection, en % du maximum (M1d §2.3).</summary>
    public int HealthPercent { get; init; }
}
