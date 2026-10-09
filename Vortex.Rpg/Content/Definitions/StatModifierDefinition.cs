namespace Vortex.Rpg.Content.Definitions;

/// <summary>One stat a buff or debuff changes while it lasts (spec M1b §7.3).</summary>
public sealed record StatModifierDefinition
{
    public required ModifierStat Stat { get; init; }

    public double Percent { get; init; }

    public double Flat { get; init; }
}
