namespace Vortex.Rpg.Content.Definitions;

/// <summary>What a skill aims at (spec M1b §6.6). Values are never renumbered.</summary>
public enum SkillTarget
{
    Self = 1,
    Ally = 2,
    Enemy = 3,
    Ground = 4,
    AroundCaster = 5,
    AroundTarget = 6,

    /// <summary>Un allié KO (M1d §2.3) : la seule voie vers un joueur au sol.</summary>
    DeadAlly = 7,
}
