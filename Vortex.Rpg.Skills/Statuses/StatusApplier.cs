using System;
using System.Linq;
using Microsoft.Extensions.Options;
using Vortex.Primitives.Observability;
using Vortex.Rpg.Combat.Combatants;
using Vortex.Rpg.Combat.Zones;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Skills.Configuration;

namespace Vortex.Rpg.Skills.Statuses;

/// <summary>Puts statuses on fighters and takes them off (spec M1b §7.1): the stacking rules, the cap,
/// the fight they start; the target's stats recomputed each time.</summary>
public sealed class StatusApplier(IOptions<SkillsConfig> config, IVortexMetrics metrics)
{
    private readonly SkillsConfig _config = config.Value;

    /// <summary>False when the target's rank ignores it (spec M1c §7.1) or the rule keeps the one
    /// already there (ignore). <paramref name="permanent"/>: until taken off, whatever its rule.</summary>
    public bool Apply(
        CombatZone zone,
        Combatant caster,
        Combatant target,
        StatusDefinition status,
        long now,
        bool permanent = false
    )
    {
        if (IsImmune(target, status))
        {
            return false;
        }

        StatusSet set = SetOf(target);
        ActiveStatus? existing = set.Find(status.Id);

        switch (permanent ? StatusStacking.Replace : status.Stacking)
        {
            case StatusStacking.Ignore when existing is not null:
                return false;
            case StatusStacking.Refresh when existing is not null:
                existing.EndsAtMs = now + status.DurationMs;
                set.Changed = true;
                break;
            case StatusStacking.Stack when existing is not null:
                existing.Stacks = Math.Min(status.MaxStacks, existing.Stacks + 1);
                existing.EndsAtMs = now + status.DurationMs;
                set.Changed = true;
                break;
            default:
                // Replace, or a first application.
                if (existing is not null)
                {
                    set.Remove(existing);
                }

                Put(
                    set,
                    status,
                    caster.ObjectId,
                    now,
                    status.DurationMs,
                    1,
                    Amount(status, caster),
                    permanent
                );
                break;
        }

        StartsAFight(zone, caster, target, status, now);
        zone.Recompute(target);
        metrics.StatusApplied(status.Id);

        // A stun breaks a cast the moment it lands (spec M1b §7.2), not at the caster's next turn.
        if (status.Type == StatusType.Stun)
        {
            zone.Interrupt(target);
        }

        return true;
    }

    /// <summary>A status carried back from another zone (spec M1b §7.4), as it was.</summary>
    public void Restore(
        CombatZone zone,
        Combatant target,
        StatusDefinition status,
        int remainingMs,
        int stacks,
        double amount,
        long now
    )
    {
        Put(SetOf(target), status, target.ObjectId, now, remainingMs, stacks, amount);
        zone.Recompute(target);
    }

    /// <summary>Up to <paramref name="count"/> dispellable statuses, the newest first: buffs from an
    /// enemy, debuffs (debuff, dot, stun, slow) from an ally. How many went.</summary>
    public int Dispel(CombatZone zone, Combatant target, bool buffs, int count)
    {
        if (target.Get<StatusSet>() is not { } set)
        {
            return 0;
        }

        ActiveStatus[] gone =
        [
            .. set
                .All.Where(s => s.Definition.Dispellable && IsBuff(s.Definition) == buffs)
                .OrderByDescending(s => s.Sequence)
                .Take(count),
        ];

        foreach (ActiveStatus status in gone)
        {
            set.Remove(status);
        }

        zone.Recompute(target);

        return gone.Length;
    }

    public void Clear(CombatZone zone, Combatant c)
    {
        if (c.Get<StatusSet>() is { } set)
        {
            set.Clear();
            zone.Recompute(c);
        }
    }

    /// <summary>The target's rank ignores this type of status (spec M1c §7.1).</summary>
    public static bool IsImmune(Combatant target, StatusDefinition status) =>
        target.Immunities.Contains(status.Type);

    /// <summary>What helps its carrier: buffs, heals over time, shields.</summary>
    public static bool IsBuff(StatusDefinition status) =>
        status.Type is StatusType.Buff or StatusType.Hot or StatusType.Shield;

    private void Put(
        StatusSet set,
        StatusDefinition status,
        int casterObjectId,
        long now,
        int durationMs,
        int stacks,
        double amount,
        bool permanent = false
    )
    {
        set.Add(
            new ActiveStatus
            {
                Definition = status,
                CasterObjectId = casterObjectId,
                EndsAtMs = permanent ? long.MaxValue : now + durationMs,
                Permanent = permanent,
                NextPulseAtMs = now + status.PeriodMs,
                Stacks = stacks,
                Amount = amount,
                Sequence = set.NextSequence(),
            }
        );

        // Past the cap, the oldest goes (spec M1b §7.1).
        while (set.All.Count > _config.MaxStatusesPerCombatant)
        {
            set.Remove(set.All.MinBy(s => s.Sequence)!);
        }
    }

    private static double Amount(StatusDefinition status, Combatant caster) =>
        status.Base + status.Coefficient * caster.Power;

    /// <summary>A harmful status on the other side is a hostile act (spec M1b §7.1): both in combat,
    /// a monster threatened by its caster.</summary>
    private static void StartsAFight(
        CombatZone zone,
        Combatant caster,
        Combatant target,
        StatusDefinition status,
        long now
    )
    {
        if (IsBuff(status) || caster.IsPlayer == target.IsPlayer)
        {
            return;
        }

        zone.Hostile(caster, now);
        zone.Hostile(target, now);
        zone.AddThreat(target, caster.ObjectId, 1);
    }

    private static StatusSet SetOf(Combatant c)
    {
        if (c.Get<StatusSet>() is not { } set)
        {
            set = new StatusSet();
            c.Set(set);
        }

        return set;
    }
}
