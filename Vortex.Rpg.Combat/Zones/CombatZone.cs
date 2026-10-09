using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans;
using Vortex.Logging.Extensions;
using Vortex.Primitives.Events;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rpg.Characters;
using Vortex.Protocol.Messages.Outgoing.Rpg.Combat;
using Vortex.Rpg.Combat.Combatants;
using Vortex.Rpg.Combat.Configuration;
using Vortex.Rpg.Combat.Hits;
using Vortex.Rpg.Combat.Stats;
using Vortex.Rpg.Combat.Systems;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Zones;

namespace Vortex.Rpg.Combat.Zones;

/// <summary>
/// A zone's fights (spec M1a §6–§8): who is in, who attacks whom, health and mana while in the
/// zone. Runs inside the zone's turn; acts on the zone through <see cref="IZoneRuntime"/>. Its parts:
/// the hits (<c>.Swings.cs</c>), deaths and respawns (<c>.Deaths.cs</c>), what a tick sends
/// (<c>.Outbox.cs</c>).
/// </summary>
public sealed partial class CombatZone(
    IZoneRuntime runtime,
    HitResolver hits,
    CombatConfig config,
    IGrainFactory grains,
    IEventPublisher events,
    IVortexMetrics metrics,
    ILogger logger,
    IEnumerable<ICombatSystem>? systems = null,
    IEnumerable<IStatModifierSource>? modifiers = null
)
{
    /// <summary>By order, then full type name: a tie never depends on the declaration order.</summary>
    private readonly ICombatSystem[] _systems =
    [
        .. (systems ?? [])
            .OrderBy(s => s.Order)
            .ThenBy(s => s.GetType().FullName, StringComparer.Ordinal),
    ];

    private readonly IStatModifierSource[] _modifiers = [.. modifiers ?? []];

    private readonly Dictionary<int, Combatant> _combatants = [];
    private readonly Dictionary<PlayerId, int> _players = [];

    /// <summary>Where each player was last sent to reach his target.</summary>
    private readonly Dictionary<int, (int X, int Y)> _approachGoals = [];

    private readonly List<int> _died = [];

    public IReadOnlyDictionary<int, Combatant> Combatants => _combatants;

    public bool HasPlayers => _players.Count > 0;

    /// <summary>The players in the zone with a character, by room object id.</summary>
    public IEnumerable<Combatant> Players => _players.Values.Select(id => _combatants[id]);

    /// <summary>Monsters that died during the last tick, for whoever brings them back.</summary>
    public IReadOnlyList<int> DiedMonsters => _died;

    public async Task PlayerEnteredAsync(PlayerId player, int objectId, CancellationToken ct)
    {
        // A resume within the grace period keeps the avatar: health was never given back.
        if (_players.ContainsKey(player))
        {
            return;
        }

        CharacterCombatProfile? profile;

        try
        {
            profile = await grains.GetPlayerCharacterGrain(player).GetCombatProfileAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "The character of player {PlayerId} did not answer: he does not fight here.",
                player.Value
            );

            return;
        }

        // An avatar without a character does not fight.
        if (profile is null)
        {
            return;
        }

        Combatant c = new()
        {
            ObjectId = objectId,
            Kind = CombatantKind.Player,
            PlayerId = player,
            Level = profile.Level,
            Range = profile.Range,
            ThreatMultiplier = profile.ThreatMultiplier,
            ClassId = profile.ClassId,
            SpecializationId = profile.SpecializationId,
        };
        Apply(c, profile.Stats);

        // A character written at 0 (it left on the tick it died) comes back as from a respawn.
        c.Health = profile.Health > 0 ? Math.Min(profile.Health, c.MaxHealth) : RespawnHealth(c);
        c.Mana = Math.Clamp(profile.Mana, 0, c.MaxMana);

        // The first one in gets nothing queued while the zone was empty (a monster removed then).
        if (!HasPlayers)
        {
            ClearOutbox();
        }

        _combatants[objectId] = c;
        _players[player] = objectId;
        _vitalsChanged.Add(objectId);

        foreach (ICombatSystem system in _systems)
        {
            system.Added(this, c, profile);
        }

        await runtime.SendToPlayerAsync(player, SelfVitals(c));
    }

    /// <summary>Health and mana go back to the character, once: awaited, so the next zone reads
    /// them (spec M1a §4.4).</summary>
    public async Task PlayerLeftAsync(PlayerId player, CancellationToken ct)
    {
        if (!_players.TryGetValue(player, out int objectId))
        {
            return;
        }

        // Still in the zone while the systems see him go: a cast ends where everyone sees it.
        Combatant c = _combatants[objectId];
        List<CarriedStatus> carried = Removing(c, CombatantExit.Left);
        _players.Remove(player);
        _combatants.Remove(objectId);
        Forget(objectId);
        LeaveCombat(c);

        foreach (Combatant m in _combatants.Values)
        {
            m.Participants.Remove(player);
        }

        if (_players.Count == 0)
        {
            ClearOutbox();
        }

        int health = c.IsGhost || c.IsDead ? RespawnHealth(c) : c.Health;

        try
        {
            await grains
                .GetPlayerCharacterGrain(player)
                .SetVitalsAsync(health, c.Mana, [.. carried]);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "Health and mana of player {PlayerId} could not be written back.",
                player.Value
            );
        }
    }

    public Combatant AddMonster(int objectId, int level, MonsterDefinition definition)
    {
        int above = Math.Max(0, level - 1);
        MonsterStatBlock b = definition.Base;
        MonsterStatBlock l = definition.PerLevel;

        Combatant c = new()
        {
            ObjectId = objectId,
            Kind = CombatantKind.Monster,
            Level = level,
            DefinitionId = definition.Id,
            Xp = definition.Xp + definition.XpPerLevel * above,
            CorpseMs = Math.Max(0, definition.CorpseSeconds) * 1000,
            MaxHealth = Math.Max(1, b.Health + l.Health * above),
            DamageMin = b.DamageMin + l.DamageMin * above,
            DamageMax = b.DamageMax + l.DamageMax * above,
            Range = Math.Max(1, definition.AttackRange),
            MonsterIntervalMs = Math.Max(
                config.MinAttackIntervalMs / 2,
                definition.AttackIntervalMs
            ),
        };

        // Its power, for a skill or a status it brings, is its average damage (plan M1b decision 4).
        c.Base = new CombatantBase
        {
            Power = (c.DamageMin + c.DamageMax) / 2,
            Armor = b.Armor + l.Armor * above,
            CritPercent = b.CritPercent + l.CritPercent * above,
            DodgePercent = b.DodgePercent + l.DodgePercent * above,
        };
        Recompute(c);
        c.Health = c.MaxHealth;

        _combatants[objectId] = c;
        _vitalsChanged.Add(objectId);

        foreach (ICombatSystem system in _systems)
        {
            system.Added(this, c, null);
        }

        return c;
    }

    /// <summary>A monster leaves the zone, its avatar with it.</summary>
    public void RemoveMonster(int objectId)
    {
        if (_combatants.TryGetValue(objectId, out Combatant? c) && !c.IsPlayer)
        {
            Removing(c, CombatantExit.Left);
            _combatants.Remove(objectId);
            Forget(objectId);
            runtime.RemoveMonster(objectId);
        }
    }

    /// <summary>The player attacks a monster (spec M1a §6.1); a refusal code, or null.</summary>
    public string? StartAttack(PlayerId player, int monsterId)
    {
        if (
            !_players.TryGetValue(player, out int id)
            || _combatants[id] is { } me && (me.IsDead || me.IsGhost)
            || !_combatants.TryGetValue(monsterId, out Combatant? target)
            || target.IsPlayer
            || target.IsDead
        )
        {
            return RpgCombatResultMessageComposer.Dead;
        }

        if (IsSafe(id) || IsSafe(monsterId))
        {
            return RpgCombatResultMessageComposer.SafeZone;
        }

        _combatants[id].TargetId = monsterId;
        _approachGoals.Remove(id);

        return null;
    }

    public void StopAttack(PlayerId player)
    {
        if (_players.TryGetValue(player, out int id))
        {
            _combatants[id].TargetId = null;
            _approachGoals.Remove(id);
        }
    }

    /// <summary>A monster back at its spawn heals entirely (spec §5.3).</summary>
    public void HealFully(int objectId)
    {
        if (_combatants.TryGetValue(objectId, out Combatant? c) && c.Health < c.MaxHealth)
        {
            c.Health = c.MaxHealth;
            _vitalsChanged.Add(objectId);
        }
    }

    /// <summary>A monster's AI decides whom it attacks.</summary>
    public void SetMonsterTarget(int monsterId, int? playerObjectId)
    {
        if (_combatants.TryGetValue(monsterId, out Combatant? m) && !m.IsPlayer)
        {
            m.TargetId = playerObjectId;
        }
    }

    public void PlayerStatsChanged(PlayerId player, CharacterCombatProfile profile)
    {
        if (!_players.TryGetValue(player, out int id))
        {
            return;
        }

        // The profile's health and mana are not read: here the zone's are the truth.
        Combatant c = _combatants[id];
        c.Level = profile.Level;
        c.Range = profile.Range;
        c.ThreatMultiplier = profile.ThreatMultiplier;
        c.ClassId = profile.ClassId;
        c.SpecializationId = profile.SpecializationId;
        Apply(c, profile.Stats);
        c.Health = Math.Min(c.Health, c.MaxHealth);
        c.Mana = Math.Min(c.Mana, c.MaxMana);
        _vitalsChanged.Add(id);
        _selfChanged.Add(id);
    }

    public async Task TickAsync(CancellationToken ct)
    {
        _died.Clear();
        long now = runtime.NowMs;

        // Before the gate: a body leaves on time even after the last player went.
        RemoveCorpses(now);

        // INV-003: a zone without a player does not simulate.
        if (!HasPlayers)
        {
            return;
        }

        // Statuses, casts: before the hits, so a stun or a cast begun stops this tick's swing.
        foreach (ICombatSystem system in _systems)
        {
            system.Tick(this, now);
        }

        Swings(now);
        await DeathsAsync(now);
        CombatExits(now);
        Regenerate(now);

        // After the regeneration: a ghost stands up at exactly its respawn health.
        Respawns(now);
        await FlushAsync();
    }

    public IEnumerable<IComposer> EntryVitals(PlayerId player)
    {
        yield return new RpgVitalsMessageComposer
        {
            Combatants = [.. _combatants.Values.Select(Vitals)],
        };

        if (_players.TryGetValue(player, out int id))
        {
            yield return SelfVitals(_combatants[id]);
        }

        foreach (IComposer composer in _systems.SelectMany(s => s.Entry(this)))
        {
            yield return composer;
        }
    }

    private void Apply(Combatant c, CharacterStatsSnapshot stats)
    {
        c.MaxHealth = Math.Max(1, stats.MaxHealth);
        c.MaxMana = Math.Max(0, stats.MaxMana);
        c.Base = new CombatantBase
        {
            Power = stats.Power,
            Armor = stats.Armor,
            CritPercent = stats.CritPercent,
            DodgePercent = stats.DodgePercent,
            SpeedPercent = stats.SpeedPercent,
        };
        Recompute(c);
    }

    private int RespawnHealth(Combatant c) =>
        Math.Max(1, c.MaxHealth * config.RespawnHealthPercent / 100);

    private bool IsSafe(int objectId) =>
        runtime.PositionOf(objectId) is { } at && runtime.RulesAt(at.X, at.Y).Safe;

    /// <summary>Nobody aims at, nor keeps threat on, a combatant gone or dead.</summary>
    private void Forget(int objectId)
    {
        _approachGoals.Remove(objectId);

        foreach (Combatant other in _combatants.Values)
        {
            other.Threat.Remove(objectId);

            if (other.TargetId == objectId)
            {
                other.TargetId = null;
            }
        }
    }

    private void Publish(IEvent e) =>
        events.PublishAsync(e).LogAndForget(logger, "Publishing {Event}", e.GetType().Name);
}
