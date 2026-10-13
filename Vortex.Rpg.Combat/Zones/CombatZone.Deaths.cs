using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vortex.Logging.Extensions;
using Vortex.Primitives.Events.Combat;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;
using Vortex.Protocol.Messages.Incoming.Rpg.Combat;
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
        c.KoSinceMs = now;

        if (c.AutoAttacking)
        {
            c.AutoAttacking = false;
            _autoAttackChanged.Add(c.ObjectId);
        }

        c.GhostUntilMs = now + config.AutoReleaseSeconds * 1000L;
        c.Waiting = false;
        c.Offer = null;
        c.DeathChoice = null;
        c.TargetId = null;
        // A death ends every cast and status (spec M1b §6.4, §7.4); a ghost carries nothing.
        Removing(c, CombatantExit.Died);
        // Whatever the systems let go (a stun, a slow) stops counting; before the posture, so the
        // ghost's lock wins.
        Recompute(c);
        runtime.StopWalking(c.ObjectId);
        runtime.SetPosture(c.ObjectId, Lay);
        Forget(c.ObjectId);
        Died(c.ObjectId);
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
        Died(c.ObjectId);
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

    /// <summary>Un mort n'est plus la cible choisie de personne : la salle vide la sienne (M1d §2.3).</summary>
    private void Died(int objectId)
    {
        runtime.TargetDied(objectId);

        foreach (PlayerId p in _selected.Where(kv => kv.Value == objectId).Select(kv => kv.Key).ToArray())
        {
            _selected.Remove(p);
        }
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

    /// <summary>La machine KO (M1d §2.3) : accepter une offre relève sur place ; relâcher sur
    /// demande, à l'auto-release ou au plafond relève au point de la zone.</summary>
    private void Respawns(long now)
    {
        foreach (Combatant c in _combatants.Values)
        {
            if (c.KoSinceMs is not { } since)
            {
                continue;
            }

            if (
                c.DeathChoice == RpgDeathChoiceMessage.Accept
                && c.Offer is { } offer
                && now < offer.ExpiresAtMs
            )
            {
                StandUp(c, c.MaxHealth * offer.HealthPercent / 100, c.MaxMana * offer.HealthPercent / 100);

                continue;
            }

            bool release =
                c.DeathChoice == RpgDeathChoiceMessage.Release
                || !c.Waiting && c.GhostUntilMs is { } until && now >= until
                || now >= since + config.MaxKoSeconds * 1000L;

            if (!release)
            {
                continue;
            }

            if (runtime.RespawnCell() is { } cell)
            {
                runtime.Teleport(c.ObjectId, cell.X, cell.Y);
            }

            StandUp(c, RespawnHealth(c), c.Mana);
        }
    }

    private void StandUp(Combatant c, int health, int mana)
    {
        c.KoSinceMs = null;
        c.GhostUntilMs = null;
        c.Waiting = false;
        c.Offer = null;
        c.DeathChoice = null;
        c.Health = Math.Max(1, health);
        c.Mana = mana;
        LeaveCombat(c);
        runtime.SetPosture(c.ObjectId, null);
        _vitalsChanged.Add(c.ObjectId);
        _selfChanged.Add(c.ObjectId);
    }
}
