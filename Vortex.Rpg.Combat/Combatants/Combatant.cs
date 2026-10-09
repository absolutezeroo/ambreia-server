using System;
using System.Collections.Generic;
using Vortex.Primitives.Players;
using Vortex.Rpg.Combat.Hits;

namespace Vortex.Rpg.Combat.Combatants;

/// <summary>
/// A fighter in a zone (spec M1a §4.4, §6): a player or a monster, its health and mana, its attack,
/// its target and timers. Owned by the zone's combat, in memory only.
/// </summary>
public sealed class Combatant : ICombatantStats
{
    private double _crit;
    private double _dodge;

    public required int ObjectId { get; init; }

    public required CombatantKind Kind { get; init; }

    /// <summary>Null for a monster.</summary>
    public PlayerId? PlayerId { get; init; }

    public bool IsPlayer => Kind == CombatantKind.Player;

    public int Level { get; set; }

    public int Health { get; set; }

    public int MaxHealth { get; set; }

    public int Mana { get; set; }

    public int MaxMana { get; set; }

    public int Power { get; set; }

    public int DamageMin { get; set; }

    public int DamageMax { get; set; }

    public int Armor { get; set; }

    /// <summary>Bounded to 0..100 whatever the content or the level adds.</summary>
    public double CritPercent
    {
        get => _crit;
        set => _crit = Math.Clamp(value, 0, 100);
    }

    public double DodgePercent
    {
        get => _dodge;
        set => _dodge = Math.Clamp(value, 0, 100);
    }

    /// <summary>Attack range in tiles (Chebyshev).</summary>
    public int Range { get; set; } = 1;

    public int AttackIntervalMs { get; set; } = 2000;

    public int ThreatMultiplier { get; set; } = 1;

    /// <summary>The combatant it attacks, by room object id.</summary>
    public int? TargetId { get; set; }

    public long NextSwingAtMs { get; set; }

    public long LastHostileAtMs { get; set; }

    public bool InCombat { get; set; }

    /// <summary>A dead player, until he respawns.</summary>
    public long? GhostUntilMs { get; set; }

    /// <summary>A monster going home: no hit lands on it.</summary>
    public bool Invulnerable { get; set; }

    public bool IsDead => Health <= 0;

    public bool IsGhost => GhostUntilMs is not null;

    /// <summary>A monster's threat per player avatar.</summary>
    public ThreatTable Threat { get; } = new();

    /// <summary>Players who hit this monster: each gets its XP when it dies.</summary>
    public HashSet<PlayerId> Participants { get; } = [];
}
