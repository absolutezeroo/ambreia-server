namespace Vortex.Rpg.Skills.Configuration;

/// <summary>Skills tuning (spec M1b §11), section <c>Vortex:Skills</c>. The action bar's size is the
/// character's (<c>Vortex:Characters:ActionBarSize</c>, plan M1b decision 5).</summary>
public sealed class SkillsConfig
{
    public const string SECTION_NAME = "Vortex:Skills";

    /// <summary>Shared by every skill subject to it, started when one is cast.</summary>
    public int GlobalCooldownMs { get; init; } = 1000;

    /// <summary>Of a heal's amount, added as threat to each monster threatened by the healed.</summary>
    public int HealThreatPercent { get; init; } = 50;

    /// <summary>Past it, the oldest status of a fighter goes.</summary>
    public int MaxStatusesPerCombatant { get; init; } = 32;
}
