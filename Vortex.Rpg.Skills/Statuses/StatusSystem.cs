using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.Extensions.Options;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Rpg.Characters;
using Vortex.Protocol.Messages.Outgoing.Rpg.Skills;
using Vortex.Rpg.Combat.Combatants;
using Vortex.Rpg.Combat.Hits;
using Vortex.Rpg.Combat.Stats;
using Vortex.Rpg.Combat.Systems;
using Vortex.Rpg.Combat.Zones;
using Vortex.Rpg.Content;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Skills.Configuration;
using Vortex.Rpg.Skills.Zones;

namespace Vortex.Rpg.Skills.Statuses;

/// <summary>
/// Statuses in a zone (spec M1b §7): each tick, pulses (damage and heal over time) then expiries;
/// while they last, their modifiers, slows and stuns on the fighter's stats. A death drops them all;
/// leaving carries the persistent ones out to the character, and entering brings them back.
/// </summary>
public sealed class StatusSystem(
    HitResolver hits,
    IOptions<SkillsConfig> config,
    IRpgContent content,
    StatusApplier applier
) : IStatusSystem, IStatModifierSource
{
    private readonly SkillsConfig _config = config.Value;

    public int Order => 100;

    public void Apply(
        CombatZone zone,
        Combatant source,
        Combatant target,
        string statusId,
        bool permanent
    )
    {
        if (content.Get<StatusDefinition>(statusId) is { Retired: false } status)
        {
            applier.Apply(zone, source, target, status, zone.Runtime.NowMs, permanent);
        }
    }

    public void Remove(CombatZone zone, Combatant target, string statusId)
    {
        if (target.Get<StatusSet>() is { } set && set.Find(statusId) is { } active)
        {
            set.Remove(active);
            zone.Recompute(target);
        }
    }

    public void Tick(CombatZone zone, long now)
    {
        foreach (Combatant c in zone.Combatants.Values.ToArray())
        {
            if (c.Get<StatusSet>() is not { } set || set.All.Count == 0)
            {
                continue;
            }

            bool ended = false;

            foreach (ActiveStatus status in set.All.ToArray())
            {
                Pulses(zone, c, set, status, now);

                if (status.EndsAtMs <= now || !set.All.Contains(status))
                {
                    set.Remove(status);
                    ended = true;
                }
            }

            if (ended)
            {
                zone.Recompute(c);
            }
        }
    }

    public void Contribute(Combatant c, StatModifiers into)
    {
        if (c.Get<StatusSet>() is not { } set)
        {
            return;
        }

        foreach (ActiveStatus status in set.All)
        {
            StatusDefinition d = status.Definition;

            switch (d.Type)
            {
                case StatusType.Buff or StatusType.Debuff:
                    foreach (StatModifierDefinition m in d.Modifiers)
                    {
                        into.AddFlat(Stat(m.Stat), m.Flat * status.Stacks);
                        into.AddPercent(Stat(m.Stat), m.Percent * status.Stacks);
                    }

                    break;
                case StatusType.Stun:
                    into.Stunned = true;
                    break;
                case StatusType.Slow:
                    into.Slow(d.SlowPercent);
                    break;
            }
        }
    }

    public void Added(CombatZone zone, Combatant c, CharacterCombatProfile? profile)
    {
        foreach (CarriedStatus carried in profile?.Statuses ?? [])
        {
            if (content.Get<StatusDefinition>(carried.StatusId) is { Retired: false } status)
            {
                applier.Restore(
                    zone,
                    c,
                    status,
                    carried.RemainingMs,
                    carried.Stacks,
                    carried.Amount,
                    zone.Runtime.NowMs
                );
            }
        }
    }

    public IEnumerable<CarriedStatus> Removing(CombatZone zone, Combatant c, CombatantExit exit)
    {
        if (c.Get<StatusSet>() is not { } set)
        {
            return [];
        }

        long now = zone.Runtime.NowMs;
        CarriedStatus[] carried =
            exit == CombatantExit.Left && c.IsPlayer
                ?
                [
                    .. set
                        .All.Where(s => s.Definition.Persistent && s.EndsAtMs > now)
                        .Select(s => new CarriedStatus
                        {
                            StatusId = s.Definition.Id,
                            RemainingMs = (int)(s.EndsAtMs - now),
                            Stacks = s.Stacks,
                            Amount = s.Amount,
                        }),
                ]
                : [];

        // A monster's body stays shown a while: told it carries nothing now.
        if (exit == CombatantExit.Died && !c.IsPlayer && set.All.Count > 0)
        {
            zone.GetOrAdd<ClearedFighters>().ObjectIds.Add(c.ObjectId);
        }

        applier.Clear(zone, c);

        return carried;
    }

    public void BeforeFlush(CombatZone zone)
    {
        ClearedFighters cleared = zone.GetOrAdd<ClearedFighters>();
        ImmutableArray<RpgCombatantStatuses> changed =
        [
            .. zone
                .Combatants.Values.Where(c => c.Get<StatusSet>() is { Changed: true })
                .Select(c => Entries(c, zone.Runtime.NowMs)),
            .. cleared.ObjectIds.Select(id => new RpgCombatantStatuses
            {
                ObjectId = id,
                Statuses = [],
            }),
        ];
        cleared.ObjectIds.Clear();

        foreach (Combatant c in zone.Combatants.Values)
        {
            if (c.Get<StatusSet>() is { } set)
            {
                set.Changed = false;
            }
        }

        if (changed.Length > 0)
        {
            zone.Broadcast(new RpgStatusesMessageComposer { Combatants = changed });
        }

        zone.GetOrAdd<SkillEvents>().Flush(zone);
    }

    public IEnumerable<IComposer> Entry(CombatZone zone, Combatant? player)
    {
        ImmutableArray<RpgCombatantStatuses> carrying =
        [
            .. zone
                .Combatants.Values.Where(c => c.Get<StatusSet>() is { All.Count: > 0 })
                .Select(c => Entries(c, zone.Runtime.NowMs)),
        ];

        return carrying.Length > 0
            ? [new RpgStatusesMessageComposer { Combatants = carrying }]
            : [];
    }

    /// <summary>Each pulse due, until the status ends; a status whose caster is gone stops (plan M1b,
    /// a ruling: no damage or heal without someone to count it for).</summary>
    private void Pulses(CombatZone zone, Combatant c, StatusSet set, ActiveStatus status, long now)
    {
        StatusDefinition d = status.Definition;

        if (d.Type is not (StatusType.Dot or StatusType.Hot))
        {
            return;
        }

        while (status.NextPulseAtMs <= now && status.NextPulseAtMs <= status.EndsAtMs && !c.IsDead)
        {
            // Its caster gone or dead (a ghost): it stops, and nobody is angered for it.
            if (
                !zone.Combatants.TryGetValue(status.CasterObjectId, out Combatant? caster)
                || caster.IsDead
                || caster.IsGhost
            )
            {
                set.Remove(status);

                return;
            }

            status.NextPulseAtMs += d.PeriodMs;
            double amount = status.Amount * status.Stacks;

            if (d.Type == StatusType.Dot)
            {
                Damage(zone, caster, c, d.Id, amount, now);
            }
            else
            {
                Heal(zone, caster, c, d.Id, amount);
            }
        }
    }

    private void Damage(
        CombatZone zone,
        Combatant caster,
        Combatant target,
        string sourceId,
        double amount,
        long now
    )
    {
        // A monster walking home takes nothing, and nothing is shown as taken.
        if (target.Invulnerable)
        {
            return;
        }

        (int dealt, int absorbed, _) = hits.Resolve(
            caster,
            target,
            zone.Runtime.Random,
            new HitOptions
            {
                BaseDamage = amount,
                Unavoidable = true,
                NoCrit = true,
            }
        );
        zone.Damage(caster, target, dealt, now);
        zone.GetOrAdd<SkillEvents>()
            .AddOutcome(Outcome(caster, target, sourceId, dealt, absorbed, RpgSkillOutcome.Hit));
    }

    private void Heal(
        CombatZone zone,
        Combatant caster,
        Combatant target,
        string sourceId,
        double amount
    )
    {
        int healed = zone.Heal(target, (int)Math.Round(amount, MidpointRounding.AwayFromZero));
        HealThreat.Spread(zone, caster, target, healed, _config);
        zone.GetOrAdd<SkillEvents>()
            .AddOutcome(Outcome(caster, target, sourceId, healed, 0, RpgSkillOutcome.Heal));
    }

    private static RpgSkillOutcome Outcome(
        Combatant source,
        Combatant target,
        string sourceId,
        int amount,
        int absorbed,
        string outcome
    ) =>
        new()
        {
            SourceObjectId = source.ObjectId,
            SourceId = sourceId,
            TargetObjectId = target.ObjectId,
            Amount = amount,
            Absorbed = absorbed,
            Outcome = outcome,
            Health = target.Health,
            MaxHealth = target.MaxHealth,
            Killed = target.Health == 0,
        };

    private static RpgCombatantStatuses Entries(Combatant c, long now) =>
        new()
        {
            ObjectId = c.ObjectId,
            Statuses =
            [
                .. (c.Get<StatusSet>()?.All ?? [])
                    .OrderBy(s => s.Sequence)
                    .Select(s => new RpgStatusEntry
                    {
                        StatusId = s.Definition.Id,
                        RemainingMs = s.Permanent ? -1 : (int)Math.Max(0, s.EndsAtMs - now),
                        Stacks = s.Stacks,
                    }),
            ],
        };

    private static CombatStat Stat(ModifierStat stat) =>
        stat switch
        {
            ModifierStat.Power => CombatStat.Power,
            ModifierStat.Armor => CombatStat.Armor,
            ModifierStat.CritPercent => CombatStat.CritPercent,
            ModifierStat.DodgePercent => CombatStat.DodgePercent,
            ModifierStat.SpeedPercent => CombatStat.SpeedPercent,
            ModifierStat.DamageDealtPercent => CombatStat.DamageDealtPercent,
            ModifierStat.DamageTakenPercent => CombatStat.DamageTakenPercent,
            _ => throw new ArgumentOutOfRangeException(nameof(stat), stat, null),
        };
}
