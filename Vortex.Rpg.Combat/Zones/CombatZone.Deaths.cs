using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vortex.Logging.Extensions;
using Vortex.Primitives.Events.Combat;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;
using Vortex.Protocol.Messages.Outgoing.Rpg.Combat;
using Vortex.Rpg.Combat.Combatants;
using Vortex.Rpg.Combat.Systems;

namespace Vortex.Rpg.Combat.Zones;

/// <summary>Deaths (spec M1a §8): a player becomes a ghost, then respawns at half health with his
/// equipment worn; a monster gives its XP to each player who hit it and is still here, then its body
/// lies dead for its <see cref="Combatant.CorpseMs"/> and goes.</summary>
public sealed partial class CombatZone
{
    private const string Lay = "lay";
    private const string Dead = "ded";

    /// <summary>Killed monsters' bodies still in the room, by object id: when each one goes. No
    /// longer combatants, so nobody aims at, hits or keeps threat on them. They die with the zone's
    /// activation, the avatars with them.</summary>
    private readonly Dictionary<int, long> _corpses = [];

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
                await MonsterDiesAsync(c, now);
            }
        }
    }

    private void PlayerDies(Combatant c, long now)
    {
        PlayerId player = c.PlayerId!.Value;

        metrics.PlayerDied();
        c.GhostUntilMs = now + config.GhostSeconds * 1000L;
        c.TargetId = null;
        // A death ends every cast and status (spec M1b §6.4, §7.4); a ghost carries nothing.
        Removing(c, CombatantExit.Died);
        // Whatever the systems let go (a stun, a slow) stops counting; before the posture, so the
        // ghost's lock wins.
        Recompute(c);
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

    private async Task MonsterDiesAsync(Combatant c, long now)
    {
        metrics.MonsterKilled();
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

        Removing(c, CombatantExit.Died);
        _combatants.Remove(c.ObjectId);
        Forget(c.ObjectId);
        _died.Add(new MonsterDeath(c.ObjectId, c.DefinitionId ?? string.Empty, present));

        if (c.CorpseMs <= 0)
        {
            runtime.RemoveMonster(c.ObjectId);
        }
        else
        {
            runtime.StopWalking(c.ObjectId);
            runtime.SetPosture(c.ObjectId, Dead);
            _corpses[c.ObjectId] = now + c.CorpseMs;
        }

        logger.LogDebug(
            "Monster {MonsterId} ({ObjectId}) died in zone {ZoneId}.",
            c.DefinitionId,
            c.ObjectId,
            runtime.ZoneId.Value
        );
    }

    private void RemoveCorpses(long now)
    {
        if (_corpses.Count == 0)
        {
            return;
        }

        foreach ((int objectId, long until) in _corpses.ToArray())
        {
            if (now >= until)
            {
                _corpses.Remove(objectId);
                runtime.RemoveMonster(objectId);
            }
        }
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
