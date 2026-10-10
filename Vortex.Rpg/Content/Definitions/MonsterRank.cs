namespace Vortex.Rpg.Content.Definitions;

/// <summary>How tough a monster is (spec M1c §7.1): its rank scales its stats by configuration.
/// Values are never renumbered.</summary>
public enum MonsterRank
{
    Normal = 1,
    Elite = 2,
    Boss = 3,
}
