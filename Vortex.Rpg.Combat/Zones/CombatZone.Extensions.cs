using System;
using System.Collections.Generic;
using System.Linq;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rpg.Characters;
using Vortex.Protocol.Messages.Outgoing.Rpg.Skills;
using Vortex.Rpg.Combat.Combatants;
using Vortex.Rpg.Combat.Configuration;
using Vortex.Rpg.Combat.Stats;
using Vortex.Rpg.Combat.Systems;
using Vortex.Rpg.Zones;

namespace Vortex.Rpg.Combat.Zones;

/// <summary>
/// What the zone offers the modules that add to its fights (plan M1b, Task 4) without naming any:
/// stats recomputed from every source of modifiers, damage, heal and threat that count like a hit,
/// messages queued for the tick, and the players' casts handed to the casting system.
/// </summary>
public sealed partial class CombatZone
{
    private readonly List<IComposer> _broadcasts = [];
    private readonly List<(PlayerId Player, IComposer Composer)> _sends = [];
    private readonly Dictionary<Type, object> _parts = [];

    /// <summary>A module's own state for this zone (what its skills did this tick); gone with the
    /// zone's activation.</summary>
    public T GetOrAdd<T>()
        where T : class, new()
    {
        if (!_parts.TryGetValue(typeof(T), out object? part))
        {
            part = new T();
            _parts[typeof(T)] = part;
        }

        return (T)part;
    }

    public IZoneRuntime Runtime => runtime;

    public CombatConfig Config => config;

    public Combatant? PlayerCombatant(PlayerId player) =>
        _players.TryGetValue(player, out int id) ? _combatants[id] : null;

    /// <summary>The effective stats from the base and every source (spec M1b §7.3); the walk speed
    /// and the stun reach the zone only when they change.</summary>
    public void Recompute(Combatant c)
    {
        StatModifiers m = new();

        foreach (IStatModifierSource source in _modifiers)
        {
            source.Contribute(c, m);
        }

        c.Power = Math.Max(0, (int)Math.Round(m.Apply(CombatStat.Power, c.Base.Power)));
        c.Armor = Math.Max(0, (int)Math.Round(m.Apply(CombatStat.Armor, c.Base.Armor)));
        c.CritPercent = m.Apply(CombatStat.CritPercent, c.Base.CritPercent);
        c.DodgePercent = m.Apply(CombatStat.DodgePercent, c.Base.DodgePercent);
        c.DamageDealtPercent = m.Apply(CombatStat.DamageDealtPercent, 0);
        c.DamageTakenPercent = m.Apply(CombatStat.DamageTakenPercent, 0);

        int speed = Math.Clamp(
            (int)Math.Round(m.Apply(CombatStat.SpeedPercent, c.Base.SpeedPercent)),
            50,
            200
        );
        c.AttackIntervalMs = c.IsPlayer
            ? CombatMath.AttackIntervalMs(speed * (100 - m.SlowPercent) / 100, config)
            : c.MonsterIntervalMs * 100 / Math.Max(10, 100 - m.SlowPercent);

        if (c.SlowPercent != m.SlowPercent)
        {
            c.SlowPercent = m.SlowPercent;
            runtime.SetMoveSpeed(c.ObjectId, 100 - m.SlowPercent);
        }

        if (c.Stunned != m.Stunned)
        {
            c.Stunned = m.Stunned;
            runtime.SetRooted(c.ObjectId, m.Stunned);
        }
    }

    /// <summary>Damage that counts like a hit (spec M1a §6.3): the killer, the threat, XP to who
    /// touched it, both in combat. Health left.</summary>
    public int Damage(Combatant source, Combatant target, int amount, long now)
    {
        // A monster going home takes nothing; a dead one keeps its killer.
        if (target.Invulnerable || target.IsDead)
        {
            return target.Health;
        }

        _vitalsChanged.Add(target.ObjectId);
        target.Health = Math.Max(0, target.Health - Math.Max(0, amount));
        target.LastHitBy = source.ObjectId;

        if (!target.IsPlayer)
        {
            // A miss is still a hostile act: the monster turns on whoever tried.
            target.Threat.Add(
                source.ObjectId,
                Math.Max(1L, (long)amount * source.ThreatMultiplier)
            );

            // XP goes to who touched it (spec §8.2): a miss gives threat only.
            if (source.PlayerId is { } player && amount > 0)
            {
                target.Participants.Add(player);
            }
        }

        Hostile(source, now);
        Hostile(target, now);

        if (target.IsPlayer)
        {
            _selfChanged.Add(target.ObjectId);
        }

        return target.Health;
    }

    /// <summary>Health given back, bounded to the maximum; what was really given.</summary>
    public int Heal(Combatant target, int amount)
    {
        int before = target.Health;
        target.Health = Math.Min(target.MaxHealth, target.Health + Math.Max(0, amount));
        int healed = target.Health - before;

        if (healed > 0)
        {
            _vitalsChanged.Add(target.ObjectId);

            if (target.IsPlayer)
            {
                _selfChanged.Add(target.ObjectId);
            }
        }

        return healed;
    }

    /// <summary>Its health or mana changed outside a hit or a heal (a cast's cost): the zone and the
    /// player are told with the tick.</summary>
    public void VitalsChanged(Combatant c)
    {
        _vitalsChanged.Add(c.ObjectId);

        if (c.IsPlayer)
        {
            _selfChanged.Add(c.ObjectId);
        }
    }

    public void AddThreat(Combatant monster, int playerObjectId, long amount)
    {
        // A monster walking home is angered by nobody on the way.
        if (!monster.IsPlayer && !monster.Invulnerable && amount > 0)
        {
            monster.Threat.Add(playerObjectId, amount);
        }
    }

    /// <summary>Sent to the zone with the tick's messages.</summary>
    public void Broadcast(IComposer composer) => _broadcasts.Add(composer);

    /// <summary>Sent to the player with the tick's messages.</summary>
    public void SendTo(PlayerId player, IComposer composer) => _sends.Add((player, composer));

    public bool IsSafeCell(int x, int y) => runtime.RulesAt(x, y).Safe;

    /// <summary>A fighter's cast, handed to the casting system the modules declared.</summary>
    public string? Cast(CastRequest request) =>
        Casting is { } casting
            ? casting.Cast(this, request)
            : RpgSkillResultMessageComposer.UnknownSkill;

    public void CancelCast(PlayerId player) => Casting?.Cancel(this, player);

    public void Interrupt(Combatant c) => Casting?.Interrupt(this, c);

    /// <summary>A monster's casting starts over: no cast under way, no cooldown.</summary>
    public void ResetCasts(Combatant c) => Casting?.Reset(this, c);

    /// <summary>A status put by another module (spec M1c §7.2), through the status system declared.</summary>
    public void ApplyStatus(Combatant source, Combatant target, string statusId, bool permanent) =>
        Statuses?.Apply(this, source, target, statusId, permanent);

    public void RemoveStatus(Combatant target, string statusId) =>
        Statuses?.Remove(this, target, statusId);

    private IStatusSystem? Statuses => _systems.OfType<IStatusSystem>().LastOrDefault();

    public void PlayerWalked(PlayerId player)
    {
        if (PlayerCombatant(player) is not { } c)
        {
            return;
        }

        foreach (ICombatSystem system in _systems)
        {
            system.PlayerWalked(this, c);
        }
    }

    private ICastingSystem? Casting => _systems.OfType<ICastingSystem>().LastOrDefault();

    /// <summary>Every system's goodbye to a fighter still in the zone: what it carries out.</summary>
    private List<CarriedStatus> Removing(Combatant c, CombatantExit exit) =>
        [.. _systems.SelectMany(s => s.Removing(this, c, exit))];
}
