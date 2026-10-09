using System.Linq;
using Vortex.Rpg.Combat;
using Vortex.Rpg.Combat.Combatants;
using Vortex.Rpg.Monsters.Spawns;
using Vortex.Rpg.Zones;

namespace Vortex.Rpg.Monsters.Behaviors;

/// <summary>
/// The melee monster (spec M1a §5.3): it wanders near its spawn; aggressive (or on a hostile cell), it
/// takes on a player in its aggro radius and in sight; it chases the top of its threat table and hits
/// in reach; past its leash or with nobody left it goes home invulnerable and heals there. It never
/// goes after a player on a safe cell.
/// </summary>
public sealed class MeleeBehavior : IMonsterBehavior
{
    private const int MinWanderDelayMs = 4000;
    private const int MaxWanderDelayMs = 8000;

    public string Key => "melee";

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

        if (Distance(at, to) <= c.Range && zone.HasLineOfSight(at.X, at.Y, to.X, to.Y))
        {
            m.State = MonsterState.Fight;
            m.ChaseGoal = null;
            zone.StopWalking(m.ObjectId);

            return;
        }

        m.State = MonsterState.Chase;

        if (m.ChaseGoal == to && zone.IsWalking(m.ObjectId))
        {
            return;
        }

        switch (zone.WalkNear(m.ObjectId, to.X, to.Y, c.Range))
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

    /// <summary>Back to its spawn, invulnerable, its table emptied (spec §5.3).</summary>
    private static void GoHome(MonsterBrain brain)
    {
        LiveMonster m = brain.Monster;

        m.State = MonsterState.Return;
        m.ChaseGoal = null;
        brain.Combatant.Invulnerable = true;
        brain.Combatant.Threat.Clear();
        brain.Combat.SetMonsterTarget(m.ObjectId, null);
        brain.Runtime.WalkNear(m.ObjectId, m.Home.X, m.Home.Y, 0);
    }

    private static void ReturnHome(MonsterBrain brain, (int X, int Y) at)
    {
        LiveMonster m = brain.Monster;

        if (Distance(at, m.Home) <= 1)
        {
            brain.Combat.HealFully(m.ObjectId);
            brain.Combatant.Invulnerable = false;
            m.State = MonsterState.Idle;
            m.NextWanderAtMs = brain.Runtime.NowMs + MinWanderDelayMs;

            return;
        }

        if (!brain.Runtime.IsWalking(m.ObjectId))
        {
            brain.Runtime.WalkNear(m.ObjectId, m.Home.X, m.Home.Y, 0);
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
        int radius = m.Spawn.Source.WanderRadius;

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

    private static int Distance((int X, int Y) a, (int X, int Y) b) =>
        CombatMath.Chebyshev(a.X, a.Y, b.X, b.Y);
}
