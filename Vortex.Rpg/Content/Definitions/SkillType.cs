namespace Vortex.Rpg.Content.Definitions;

/// <summary>How a skill is cast (spec M1b §6.3). Values are never renumbered.</summary>
public enum SkillType
{
    Instant = 1,
    Cast = 2,
    Channel = 3,
}
