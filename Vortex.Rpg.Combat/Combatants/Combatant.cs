using System;
using System.Collections.Generic;
using Vortex.Primitives.Players;
using Vortex.Rpg.Combat.Hits;
using Vortex.Rpg.Combat.Stats;

namespace Vortex.Rpg.Combat.Combatants;

/// <summary>
/// A fighter in a zone (spec M1a §4.4, §6): a player or a monster, its health and mana, its attack,
/// its target and timers. Owned by the zone's combat, in memory only.
/// </summary>
public sealed class Combatant : ICombatantStats
{
    private readonly Dictionary<Type, object> _parts = [];
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

    /// <summary>Who hit it last, by room object id: the killer when it dies.</summary>
    public int? LastHitBy { get; set; }

    /// <summary>A monster's content id; null for a player.</summary>
    public string? DefinitionId { get; init; }

    /// <summary>The XP a monster gives at its level, before the level gap.</summary>
    public int Xp { get; init; }

    /// <summary>How long a monster's body stays in the room once killed; 0: it goes at once.</summary>
    public int CorpseMs { get; init; }

    /// <summary>Its stats before any modifier; the effective ones above are recomputed from it
    /// (spec M1b §7.3).</summary>
    public CombatantBase Base { get; set; } = new();

    /// <summary>A monster's own time between two hits, before a slow.</summary>
    public int MonsterIntervalMs { get; set; }

    public double DamageDealtPercent { get; set; }

    public double DamageTakenPercent { get; set; }

    /// <summary>The strongest slow on it, in percent.</summary>
    public int SlowPercent { get; set; }

    /// <summary>No hit, no cast, no walk (spec M1b §7.2).</summary>
    public bool Stunned { get; set; }

    /// <summary>An action occupies it (a cast): no automatic hit meanwhile.</summary>
    public bool Busy { get; set; }

    /// <summary>A module walks it somewhere (a cast's approach): the attack hits what it reaches but
    /// does not walk it back to its own target meanwhile.</summary>
    public bool Steered { get; set; }

    /// <summary>A player's class and specialization, for the skills he knows.</summary>
    public string ClassId { get; set; } = string.Empty;

    public string SpecializationId { get; set; } = string.Empty;

    /// <summary>A module's own state on this fighter (its statuses, its cast); gone with it.</summary>
    public T? Get<T>()
        where T : class => _parts.TryGetValue(typeof(T), out object? part) ? (T)part : null;

    /// <summary>Null removes the part.</summary>
    public void Set<T>(T? part)
        where T : class
    {
        if (part is null)
        {
            _parts.Remove(typeof(T));
        }
        else
        {
            _parts[typeof(T)] = part;
        }
    }
}
