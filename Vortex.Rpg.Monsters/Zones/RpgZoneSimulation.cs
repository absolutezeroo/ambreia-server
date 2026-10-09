using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rpg.Characters;
using Vortex.Protocol.Messages.Outgoing.Rpg.Combat;
using Vortex.Protocol.Messages.Outgoing.Rpg.Interaction;
using Vortex.Rpg.Combat.Combatants;
using Vortex.Rpg.Combat.Zones;
using Vortex.Rpg.Monsters.Behaviors;
using Vortex.Rpg.Monsters.Spawns;
using Vortex.Rpg.Zones;

namespace Vortex.Rpg.Monsters.Zones;

/// <summary>
/// A zone's fights (spec M1a §4.3): its monsters come and go, think, then the combat resolves the
/// tick. One per zone activation, made by <see cref="RpgZoneSimulationFactory"/>.
/// </summary>
public sealed class RpgZoneSimulation(IZoneRuntime zone, CombatZone combat, MonsterSpawner spawner)
    : IZoneSimulation
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
        // INV-003: no player, no simulation — no birth, no AI.
        if (!combat.HasPlayers)
        {
            return;
        }

        long now = zone.NowMs;

        spawner.Tick(now);

        foreach (LiveMonster m in spawner.Live.Values.ToArray())
        {
            if (combat.Combatants.TryGetValue(m.ObjectId, out Combatant? c))
            {
                m.Spawn.Behavior.Think(
                    new MonsterBrain
                    {
                        Monster = m,
                        Combatant = c,
                        Combat = combat,
                        Runtime = zone,
                    }
                );
            }
        }

        await combat.TickAsync(ct);

        foreach (int died in combat.DiedMonsters)
        {
            spawner.Died(died, now);
        }
    }

    public IComposer? EntryVitals() => combat.EntryVitals();

    public void PlayerStatsChanged(PlayerId player, CharacterStatsSnapshot stats) =>
        combat.PlayerStatsChanged(player, stats);
}
