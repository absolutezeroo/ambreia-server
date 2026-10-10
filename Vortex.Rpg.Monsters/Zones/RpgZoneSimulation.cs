using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rpg.Characters;
using Vortex.Protocol.Messages.Outgoing.Rpg.Combat;
using Vortex.Protocol.Messages.Outgoing.Rpg.Interaction;
using Vortex.Protocol.Messages.Outgoing.Rpg.Skills;
using Vortex.Rpg.Combat.Combatants;
using Vortex.Rpg.Combat.Systems;
using Vortex.Rpg.Combat.Zones;
using Vortex.Rpg.Content;
using Vortex.Rpg.Monsters.Behaviors;
using Vortex.Rpg.Monsters.Configuration;
using Vortex.Rpg.Monsters.Phases;
using Vortex.Rpg.Monsters.Spawns;
using Vortex.Rpg.Zones;

namespace Vortex.Rpg.Monsters.Zones;

/// <summary>
/// A zone's fights (spec M1a §4.3): its monsters come and go, think, then the combat resolves the
/// tick. One per zone activation, made by <see cref="RpgZoneSimulationFactory"/>.
/// </summary>
public sealed class RpgZoneSimulation(
    IZoneRuntime zone,
    CombatZone combat,
    MonsterSpawner spawner,
    IRpgContent content,
    IVortexMetrics metrics,
    MonstersConfig config
) : IZoneSimulation
{
    internal CombatZone Combat => combat;

    internal MonsterSpawner Spawner => spawner;

    public Task PlayerEnteredAsync(PlayerId player, int objectId, CancellationToken ct) =>
        combat.PlayerEnteredAsync(player, objectId, ct);

    public Task PlayerLeftAsync(PlayerId player, CancellationToken ct) =>
        combat.PlayerLeftAsync(player, ct);

    public async Task<bool> SelectTargetAsync(PlayerId player, int objectId, CancellationToken ct)
    {
        if (!combat.Combatants.TryGetValue(objectId, out Combatant? monster) || monster.IsPlayer)
        {
            return false;
        }

        await zone.SendToPlayerAsync(
            player,
            new RpgTargetMessageComposer
            {
                ObjectId = objectId,
                Kind = RpgTargetMessageComposer.KindMonster,
                Name = monster.DefinitionId ?? string.Empty,
                Level = monster.Level,
                ClassId = string.Empty,
                Rank = spawner.Live.TryGetValue(objectId, out LiveMonster? live)
                    ? JsonNamingPolicy.SnakeCaseLower.ConvertName(live.Definition.Rank.ToString())
                    : RpgTargetMessageComposer.RankNormal,
            }
        );

        if (combat.StartAttack(player, objectId) is { } refusal)
        {
            await zone.SendToPlayerAsync(
                player,
                new RpgCombatResultMessageComposer { Code = refusal }
            );
        }

        return true;
    }

    public void StopAttack(PlayerId player) => combat.StopAttack(player);

    public async Task TickAsync(CancellationToken ct)
    {
        // INV-003: no player, no simulation — no birth, no AI. The combat still sends its dead
        // monsters' bodies away on time.
        if (!combat.HasPlayers)
        {
            await combat.TickAsync(ct);

            return;
        }

        long now = zone.NowMs;

        spawner.Tick(now);

        foreach (LiveMonster m in spawner.Live.Values.ToArray())
        {
            // Gone this tick already: a summon its boss's reset sent away.
            if (!spawner.Live.ContainsKey(m.ObjectId))
            {
                continue;
            }

            if (!combat.Combatants.TryGetValue(m.ObjectId, out Combatant? c))
            {
                continue;
            }

            MonsterBrain brain = new()
            {
                Monster = m,
                Combatant = c,
                Combat = combat,
                Runtime = zone,
                Spawner = spawner,
                Content = content,
                Metrics = metrics,
                Config = config,
            };

            // Last tick's hits are counted: a phase plays even on a stunned boss (spec M1c §7.2).
            BossPhases.Check(brain);

            // A stunned monster does not think: no chase, no target, no way home (spec M1b §7.2).
            if (!c.Stunned)
            {
                m.Spawn.Behavior.Think(brain);
            }
        }

        try
        {
            await combat.TickAsync(ct);
        }
        finally
        {
            // Whatever the tick threw, a monster removed from the zone comes back.
            foreach (int died in combat.DiedMonsters)
            {
                spawner.Died(died, now);
            }
        }
    }

    public IEnumerable<IComposer> EntryVitals(PlayerId player) => combat.EntryVitals(player);

    public void PlayerStatsChanged(PlayerId player, CharacterCombatProfile profile) =>
        combat.PlayerStatsChanged(player, profile);

    public async Task CastSkillAsync(
        PlayerId player,
        string skillId,
        int targetObjectId,
        int x,
        int y,
        CancellationToken ct
    )
    {
        string? refusal = combat.PlayerCombatant(player) is { } me
            ? combat.Cast(new CastRequest(me.ObjectId, skillId, targetObjectId, x, y))
            : RpgSkillResultMessageComposer.Dead;

        if (refusal is not null)
        {
            await zone.SendToPlayerAsync(
                player,
                new RpgSkillResultMessageComposer { SkillId = skillId, Code = refusal }
            );
        }
    }

    public void CancelCast(PlayerId player) => combat.CancelCast(player);

    public void PlayerWalked(PlayerId player) => combat.PlayerWalked(player);
}
