using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vortex.Logging.Extensions;
using Vortex.Primitives.Events.Combat;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;
using Vortex.Protocol.Messages.Outgoing.Rpg.Combat;
using Vortex.Rpg.Combat.Combatants;

namespace Vortex.Rpg.Combat.Zones;

/// <summary>Deaths (spec M1a §8): a player becomes a ghost, then respawns at half health with his
/// equipment worn; a monster gives its XP to each player who hit it and is still here, then goes.</summary>
public sealed partial class CombatZone
{
    private const string Lay = "lay";

    private async Task DeathsAsync(long now)
    {
        foreach (Combatant c in _combatants.Values.Where(c => c.IsDead && !c.IsGhost).ToArray())
        {
            if (c.IsPlayer)
            {
                PlayerDies(c, now);
            }
            else
            {
                await MonsterDiesAsync(c);
            }
        }
    }

    private void PlayerDies(Combatant c, long now)
    {
        PlayerId player = c.PlayerId!.Value;

        c.GhostUntilMs = now + config.GhostSeconds * 1000L;
        c.TargetId = null;
        runtime.StopWalking(c.ObjectId);
        runtime.SetPosture(c.ObjectId, Lay);
        Forget(c.ObjectId);
        _selfChanged.Add(c.ObjectId);
        _deaths.Add(player);

        string? killer =
            c.LastHitBy is { } by && _combatants.TryGetValue(by, out Combatant? k)
                ? k.DefinitionId
                : null;
        Publish(new PlayerDiedEvent(player, runtime.ZoneId, killer));

        grains
            .GetPlayerCharacterGrain(player)
            .WearEquipmentAsync(config.DeathWearPercent)
            .LogAndForget(logger, "Wearing the equipment of player {PlayerId}", player.Value);

        logger.LogInformation(
            "Player {PlayerId} died in zone {ZoneId}.",
            player.Value,
            runtime.ZoneId.Value
        );
    }

    private async Task MonsterDiesAsync(Combatant c)
    {
        PlayerId[] present = [.. c.Participants.Where(_players.ContainsKey)];

        // Different characters: told together.
        await Task.WhenAll(present.Select(p => GrantXpAsync(p, c)));

        Publish(
            new MonsterKilledEvent(
                c.DefinitionId ?? string.Empty,
                c.Level,
                runtime.ZoneId,
                [.. present]
            )
        );

        _combatants.Remove(c.ObjectId);
        Forget(c.ObjectId);
        runtime.RemoveMonster(c.ObjectId);
        _died.Add(c.ObjectId);

        logger.LogDebug(
            "Monster {MonsterId} ({ObjectId}) died in zone {ZoneId}.",
            c.DefinitionId,
            c.ObjectId,
            runtime.ZoneId.Value
        );
    }

    private async Task GrantXpAsync(PlayerId player, Combatant monster)
    {
        int level = _combatants.TryGetValue(_players[player], out Combatant? p) ? p.Level : 1;
        int amount = CombatMath.XpFor(monster.Xp, monster.Level, level, config);

        if (amount <= 0)
        {
            return;
        }

        try
        {
            await grains
                .GetPlayerCharacterGrain(player)
                .GainXpAsync(amount, $"monster:{monster.DefinitionId}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "XP for player {PlayerId} could not be granted.", player.Value);
        }
    }

    /// <summary>A ghost whose time is up stands again at the zone's respawn cell (spec §8.1).</summary>
    private void Respawns(long now)
    {
        foreach (Combatant c in _combatants.Values)
        {
            if (c.GhostUntilMs is not { } until || now < until)
            {
                continue;
            }

            if (runtime.RespawnCell() is { } cell)
            {
                runtime.Teleport(c.ObjectId, cell.X, cell.Y);
            }

            c.GhostUntilMs = null;
            c.Health = RespawnHealth(c);
            LeaveCombat(c);
            runtime.SetPosture(c.ObjectId, null);
            _vitalsChanged.Add(c.ObjectId);
            _selfChanged.Add(c.ObjectId);
        }
    }
}
