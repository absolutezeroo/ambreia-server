namespace Vortex.Rpg.Combat.Configuration;

/// <summary>Combat tuning (spec M1a §10), section <c>Vortex:Combat</c>.</summary>
public sealed class CombatConfig
{
    public const string SECTION_NAME = "Vortex:Combat";

    /// <summary>How often the zone's combat step runs.</summary>
    public int TickMs { get; init; } = 250;

    /// <summary>A player's time between two hits at 100 % speed.</summary>
    public int PlayerAttackIntervalMs { get; init; } = 2000;

    /// <summary>No faster than this, whatever the speed.</summary>
    public int MinAttackIntervalMs { get; init; } = 1000;

    /// <summary>A player's base damage is Power times a roll between these two.</summary>
    public double PlayerDamageMinFactor { get; init; } = 0.85;

    public double PlayerDamageMaxFactor { get; init; } = 1.15;

    public double CritMultiplier { get; init; } = 1.5;

    /// <summary>Out of combat this long after the last hit given or taken.</summary>
    public int CombatTimeoutMs { get; init; } = 5000;

    /// <summary>Out of combat, health and mana come back by this share of their maximum per second.</summary>
    public int RegenPercentPerSecond { get; init; } = 2;

    /// <summary>Un joueur KO qui n'a rien choisi relâche après ce délai (M1d §2.3).</summary>
    public int AutoReleaseSeconds { get; init; } = 60;

    /// <summary>Même après « attendre », au plus ce temps au sol.</summary>
    public int MaxKoSeconds { get; init; } = 600;

    public int ResurrectOfferSeconds { get; init; } = 30;

    /// <summary>La menace n'est renvoyée que si elle a bougé d'autant de points.</summary>
    public int ThreatReportStep { get; init; } = 5;

    public int RespawnHealthPercent { get; init; } = 50;

    /// <summary>The share of each equipped item's max durability a death takes.</summary>
    public int DeathWearPercent { get; init; } = 10;

    /// <summary>XP change per level of difference between monster and player.</summary>
    public int XpLevelStepPercent { get; init; } = 10;

    public int XpMaxBonusPercent { get; init; } = 50;

    /// <summary>A monster this many levels below the player or more gives no XP.</summary>
    public int XpCutoffLevels { get; init; } = 8;
}
