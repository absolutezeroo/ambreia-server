using System;
using System.Linq;
using Vortex.Primitives.Events.Combat;
using Vortex.Protocol.Messages.Outgoing.Rpg.Combat;
using Vortex.Rpg.Combat.Combatants;
using Vortex.Rpg.Zones;

namespace Vortex.Rpg.Combat.Zones;

/// <summary>Hits (spec M1a §6.1, §6.2): each fighter with a target strikes at its cadence when in
/// range and in sight; a player out of range walks to it. A monster's movement is its AI's.</summary>
public sealed partial class CombatZone
{
    private void Swings(long now)
    {
        foreach (Combatant a in _combatants.Values.ToArray())
        {
            if (a.IsDead || a.IsGhost || a.TargetId is not { } targetId)
            {
                continue;
            }

            if (!_combatants.TryGetValue(targetId, out Combatant? t) || t.IsDead || t.IsGhost)
            {
                a.TargetId = null;

                continue;
            }

            if (
                runtime.PositionOf(a.ObjectId) is not { } from
                || runtime.PositionOf(targetId) is not { } to
            )
            {
                continue;
            }

            // No combat on a safe cell, on either side (spec §5.3, §6.1).
            if (runtime.RulesAt(from.X, from.Y).Safe || runtime.RulesAt(to.X, to.Y).Safe)
            {
                a.TargetId = null;

                if (a.IsPlayer)
                {
                    Refuse(a, RpgCombatResultMessageComposer.SafeZone);
                }

                continue;
            }

            bool inReach =
                CombatMath.Chebyshev(from.X, from.Y, to.X, to.Y) <= a.Range
                && runtime.HasLineOfSight(from.X, from.Y, to.X, to.Y);

            if (!inReach)
            {
                if (a.IsPlayer)
                {
                    Approach(a, to);
                }

                continue;
            }

            if (a.IsPlayer && _approachGoals.Remove(a.ObjectId))
            {
                runtime.StopWalking(a.ObjectId);
            }

            if (now < a.NextSwingAtMs || t.Invulnerable)
            {
                continue;
            }

            Hit(a, t, now);
        }
    }

    /// <summary>A player walks to his target, again only when it moved or he stopped.</summary>
    private void Approach(Combatant a, (int X, int Y) to)
    {
        if (
            _approachGoals.TryGetValue(a.ObjectId, out (int X, int Y) goal)
            && goal == to
            && runtime.IsWalking(a.ObjectId)
        )
        {
            return;
        }

        switch (runtime.WalkNear(a.ObjectId, to.X, to.Y, a.Range))
        {
            case WalkResult.Started:
                _approachGoals[a.ObjectId] = to;
                break;
            case WalkResult.NoPath:
                a.TargetId = null;
                _approachGoals.Remove(a.ObjectId);
                Refuse(a, RpgCombatResultMessageComposer.Unreachable);
                break;
        }
    }

    private void Hit(Combatant a, Combatant t, long now)
    {
        (int amount, string outcome) = hits.Resolve(a, t, runtime.Random);
        metrics.CombatHit(outcome);

        t.Health = Math.Max(0, t.Health - amount);
        t.LastHitBy = a.ObjectId;
        a.NextSwingAtMs = now + a.AttackIntervalMs;

        if (!t.IsPlayer)
        {
            // A miss is still a hostile act: the monster turns on whoever tried.
            t.Threat.Add(a.ObjectId, Math.Max(1L, (long)amount * a.ThreatMultiplier));

            // XP goes to who touched it (spec §8.2): a miss gives threat only.
            if (a.PlayerId is { } player && amount > 0)
            {
                t.Participants.Add(player);
            }
        }

        Hostile(a, now);
        Hostile(t, now);

        _hits.Add(
            new RpgCombatHit
            {
                AttackerId = a.ObjectId,
                TargetId = t.ObjectId,
                Amount = amount,
                Outcome = outcome,
                Health = t.Health,
                MaxHealth = t.MaxHealth,
                Killed = t.Health == 0,
            }
        );

        if (t.IsPlayer)
        {
            _selfChanged.Add(t.ObjectId);
        }
    }

    private void Hostile(Combatant c, long now)
    {
        c.LastHostileAtMs = now;

        if (c.InCombat)
        {
            return;
        }

        c.InCombat = true;

        if (c.PlayerId is { } player)
        {
            _selfChanged.Add(c.ObjectId);
            Publish(new CombatEnteredEvent(player));
        }
    }

    private void Refuse(Combatant player, string code)
    {
        if (player.PlayerId is { } p)
        {
            _results.Add((p, code));
        }
    }

    /// <summary>Out of combat after the timeout, unless a monster still has him in its table.</summary>
    private void CombatExits(long now)
    {
        foreach (Combatant c in _combatants.Values)
        {
            if (!c.InCombat || now - c.LastHostileAtMs < config.CombatTimeoutMs)
            {
                continue;
            }

            if (c.PlayerId is not { } player)
            {
                c.InCombat = false;

                continue;
            }

            if (_combatants.Values.Any(m => !m.IsPlayer && m.Threat.Contains(c.ObjectId)))
            {
                continue;
            }

            LeaveCombat(c);
        }
    }

    /// <summary>Out of combat, told to the player and on the bus: every path out goes here.</summary>
    private void LeaveCombat(Combatant c)
    {
        if (!c.InCombat)
        {
            return;
        }

        c.InCombat = false;

        if (c.PlayerId is { } player)
        {
            _selfChanged.Add(c.ObjectId);
            Publish(new CombatLeftEvent(player));
        }
    }

    /// <summary>Out of combat, a share of the maximum per full second (spec §6.3).</summary>
    private void Regenerate(long now)
    {
        if (now < _nextRegenAtMs)
        {
            return;
        }

        _nextRegenAtMs = now + 1000;

        foreach (Combatant c in _combatants.Values)
        {
            if (
                c.InCombat
                || c.IsDead
                || c.IsGhost
                || (c.Health >= c.MaxHealth && c.Mana >= c.MaxMana)
            )
            {
                continue;
            }

            c.Health = Math.Min(c.MaxHealth, c.Health + Share(c.MaxHealth));
            c.Mana = Math.Min(c.MaxMana, c.Mana + Share(c.MaxMana));
            _vitalsChanged.Add(c.ObjectId);

            if (c.IsPlayer)
            {
                _selfChanged.Add(c.ObjectId);
            }
        }
    }

    private int Share(int max) =>
        max <= 0 ? 0 : Math.Max(1, max * config.RegenPercentPerSecond / 100);
}
