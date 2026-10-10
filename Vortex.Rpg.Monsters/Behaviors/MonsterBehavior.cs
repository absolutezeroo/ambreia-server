using System.Linq;
using Vortex.Rpg.Combat;
using Vortex.Rpg.Combat.Combatants;
using Vortex.Rpg.Monsters.Phases;
using Vortex.Rpg.Monsters.Skills;
using Vortex.Rpg.Monsters.Spawns;
using Vortex.Rpg.Zones;

namespace Vortex.Rpg.Monsters.Behaviors;

/// <summary>
/// What every monster does (spec M1a §5.3, M1c §4.3): it wanders near its spawn; aggressive (or on a
/// hostile cell), it takes on a player in its aggro radius and in sight; it chases the top of its
/// threat table to the distance its kind wants, then acts there; past its leash or with nobody left
/// it goes home invulnerable and heals there. It never goes after a player on a safe cell, and never
/// moves while it casts. A kind decides only its distance and what it does in reach.
/// </summary>
public abstract class MonsterBehavior : IMonsterBehavior
{
    private const int MinWanderDelayMs = 4000;
    private const int MaxWanderDelayMs = 8000;

    public abstract string Key { get; }

    public void Think(MonsterBrain brain)
    {
        LiveMonster m = brain.Monster;
        Combatant c = brain.Combatant;
        IZoneRuntime zone = brain.Runtime;

        if (c.IsDead || zone.PositionOf(m.ObjectId) is not { } at)
        {
            return;
        }

        if (m.State == MonsterState.Return)
        {
            ReturnHome(brain, at);

            return;
        }

        if (Distance(at, m.Home) > m.Spawn.LeashRadius)
        {
            GoHome(brain);

            return;
        }

        // A cast holds it where it stands (spec M1c §4.3).
        if (c.Busy)
        {
            return;
        }

        DropPlayersOnSafeCells(brain);

        if (c.Threat.IsEmpty && (m.Definition.Aggressive || zone.RulesAt(at.X, at.Y).Hostile))
        {
            Acquire(brain, at);
        }

        if (c.Threat.Top is not { } top || zone.PositionOf(top) is not { } to)
        {
            brain.Combat.SetMonsterTarget(m.ObjectId, null);

            if (m.State == MonsterState.Idle)
            {
                Wander(brain);
            }
            else
            {
                GoHome(brain);
            }

            return;
        }

        brain.Combat.SetMonsterTarget(m.ObjectId, top);
        int range = DesiredRange(brain);

        if (Distance(at, to) <= range && zone.HasLineOfSight(at.X, at.Y, to.X, to.Y))
        {
            bool justStopped = zone.IsWalking(m.ObjectId);
            m.State = MonsterState.Fight;
            m.ChaseGoal = null;
            zone.StopWalking(m.ObjectId);
            Act(brain, top, justStopped);

            return;
        }

        m.State = MonsterState.Chase;

        if (m.ChaseGoal == to && zone.IsWalking(m.ObjectId))
        {
            return;
        }

        switch (zone.WalkNear(m.ObjectId, to.X, to.Y, range))
        {
            case WalkResult.Started:
                m.ChaseGoal = to;
                break;
            case WalkResult.NoPath:
                // Out of its reach: it lets that one go.
                c.Threat.Remove(top);
                m.ChaseGoal = null;
                break;
        }
    }

    /// <summary>How close it stands to its target: its attack's reach by default.</summary>
    protected virtual int DesiredRange(MonsterBrain brain) => brain.Combatant.Range;

    /// <summary>In reach of its target: a skill if one passes, else the automatic attack goes on
    /// (spec M1c §6). <paramref name="justStopped"/>: it was walking a moment ago.</summary>
    protected virtual void Act(MonsterBrain brain, int targetObjectId, bool justStopped) =>
        MonsterSkillSelector.TryCast(brain, targetObjectId);

    protected static int Distance((int X, int Y) a, (int X, int Y) b) =>
        CombatMath.Chebyshev(a.X, a.Y, b.X, b.Y);

    private static void Acquire(MonsterBrain brain, (int X, int Y) at)
    {
        IZoneRuntime zone = brain.Runtime;
        int radius = brain.Monster.Spawn.AggroRadius;

        Combatant? nearest = brain
            .Combat.Players.Where(p => !p.IsDead && !p.IsGhost)
            .Select(p => (Player: p, At: zone.PositionOf(p.ObjectId)))
            .Where(x =>
                x.At is { } to
                && !zone.RulesAt(to.X, to.Y).Safe
                && Distance(at, to) <= radius
                && zone.HasLineOfSight(at.X, at.Y, to.X, to.Y)
            )
            .OrderBy(x => Distance(at, x.At!.Value))
            .Select(x => x.Player)
            .FirstOrDefault();

        if (nearest is not null)
        {
            brain.Combatant.Threat.Add(nearest.ObjectId, 1);
        }
    }

    private static void DropPlayersOnSafeCells(MonsterBrain brain)
    {
        foreach (Combatant p in brain.Combat.Players)
        {
            if (
                brain.Combatant.Threat.Contains(p.ObjectId)
                && brain.Runtime.PositionOf(p.ObjectId) is { } to
                && brain.Runtime.RulesAt(to.X, to.Y).Safe
            )
            {
                brain.Combatant.Threat.Remove(p.ObjectId);
            }
        }
    }

    /// <summary>Back to its spawn, invulnerable, its table emptied, its casting started over (spec
    /// M1a §5.3, M1c §7.4).</summary>
    private static void GoHome(MonsterBrain brain)
    {
        LiveMonster m = brain.Monster;

        m.State = MonsterState.Return;
        m.ChaseGoal = null;
        brain.Combatant.Invulnerable = true;
        brain.Combatant.Threat.Clear();
        brain.Combat.SetMonsterTarget(m.ObjectId, null);
        BossPhases.Reset(brain);
        WalkHome(brain);
    }

    /// <summary>No path home: put back there at once rather than left invulnerable for good.</summary>
    private static void WalkHome(MonsterBrain brain)
    {
        LiveMonster m = brain.Monster;

        if (brain.Runtime.WalkNear(m.ObjectId, m.Home.X, m.Home.Y, 0) == WalkResult.NoPath)
        {
            brain.Runtime.Teleport(m.ObjectId, m.Home.X, m.Home.Y);
            Arrived(brain);
        }
    }

    private static void Arrived(MonsterBrain brain)
    {
        LiveMonster m = brain.Monster;

        brain.Combat.HealFully(m.ObjectId);
        brain.Combatant.Invulnerable = false;
        m.State = MonsterState.Idle;
        m.NextWanderAtMs = brain.Runtime.NowMs + MinWanderDelayMs;
    }

    private static void ReturnHome(MonsterBrain brain, (int X, int Y) at)
    {
        LiveMonster m = brain.Monster;

        if (Distance(at, m.Home) <= 1)
        {
            Arrived(brain);

            return;
        }

        if (!brain.Runtime.IsWalking(m.ObjectId))
        {
            WalkHome(brain);
        }
    }

    /// <summary>A step now and then to a free cell near its spawn, never a safe one.</summary>
    private static void Wander(MonsterBrain brain)
    {
        LiveMonster m = brain.Monster;
        IZoneRuntime zone = brain.Runtime;

        if (zone.NowMs < m.NextWanderAtMs)
        {
            return;
        }

        m.NextWanderAtMs = zone.NowMs + zone.Random.Next(MinWanderDelayMs, MaxWanderDelayMs);
        // Never past its leash, or it would wander off and straight back home in a loop.
        int radius = System.Math.Min(m.Spawn.Source.WanderRadius, m.Spawn.LeashRadius);

        for (int attempt = 0; attempt < 8 && radius > 0; attempt++)
        {
            int x = m.Home.X + zone.Random.Next(-radius, radius + 1);
            int y = m.Home.Y + zone.Random.Next(-radius, radius + 1);

            if (zone.IsFree(x, y) && !zone.RulesAt(x, y).Safe)
            {
                zone.WalkNear(m.ObjectId, x, y, 0);

                return;
            }
        }
    }
}
