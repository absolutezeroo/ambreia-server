using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Players;
using Vortex.Protocol.Messages.Outgoing.Rpg.Combat;
using Vortex.Rpg.Combat.Combatants;
using Vortex.Rpg.Combat.Systems;

namespace Vortex.Rpg.Combat.Zones;

/// <summary>What a tick sends (spec M1a §9.2): one message per kind and per tick, only when
/// something happened — the hits and the changed health to the zone, each player's own vitals,
/// deaths and refusals to the player alone.</summary>
public sealed partial class CombatZone
{
    private readonly List<RpgCombatHit> _hits = [];
    private readonly HashSet<int> _vitalsChanged = [];
    private readonly HashSet<int> _selfChanged = [];
    private readonly List<PlayerId> _deaths = [];
    private readonly List<(PlayerId Player, string Code)> _results = [];
    private readonly HashSet<int> _autoAttackChanged = [];
    private readonly Dictionary<int, int?> _lastTargets = [];
    private readonly Dictionary<PlayerId, (int Monster, int Percent, bool HasAggro)> _lastThreat = [];
    private long _nextRegenAtMs;

    private async Task FlushAsync()
    {
        foreach (ICombatSystem system in _systems)
        {
            system.BeforeFlush(this);
        }

        // Each composer is built now, before the outbox is cleared; only the send is deferred.
        List<Func<Task>> sends = [];

        // Qui vise qui, par différence (M1d décision 2) : six sites écrivent TargetId.
        List<RpgTargetEntry> aims = [];

        foreach (Combatant fighter in _combatants.Values)
        {
            if (
                !_lastTargets.TryGetValue(fighter.ObjectId, out int? last)
                || last != fighter.TargetId
            )
            {
                _lastTargets[fighter.ObjectId] = fighter.TargetId;
                aims.Add(
                    new RpgTargetEntry
                    {
                        ObjectId = fighter.ObjectId,
                        TargetObjectId = fighter.TargetId ?? 0,
                    }
                );
            }
        }

        foreach (int gone in _lastTargets.Keys.Where(id => !_combatants.ContainsKey(id)).ToArray())
        {
            _lastTargets.Remove(gone);
        }

        if (aims.Count > 0)
        {
            RpgTargetsMessageComposer targets = new() { Entries = [.. aims] };
            sends.Add(() => runtime.BroadcastAsync(targets));
        }

        // La menace du joueur sur sa cible (M1d §2.1) : par pas, et quand l'aggro change de main.
        foreach ((PlayerId player, int monsterId) in _selected)
        {
            if (
                !_players.TryGetValue(player, out int me)
                || !_combatants.TryGetValue(monsterId, out Combatant? m)
                || m.IsPlayer
            )
            {
                continue;
            }

            (int percent, bool hasAggro) = ThreatOf(m, me);
            bool send =
                !_lastThreat.TryGetValue(player, out (int Monster, int Percent, bool HasAggro) last)
                || last.Monster != monsterId
                || last.HasAggro != hasAggro
                || Math.Abs(last.Percent - percent) >= config.ThreatReportStep;

            if (!send)
            {
                continue;
            }

            _lastThreat[player] = (monsterId, percent, hasAggro);
            RpgThreatMessageComposer threat = new()
            {
                MonsterObjectId = monsterId,
                Percent = percent,
                HasAggro = hasAggro,
            };
            sends.Add(() => runtime.SendToPlayerAsync(player, threat));
        }

        foreach (PlayerId p in _lastThreat.Keys.Where(p => !_selected.ContainsKey(p)).ToArray())
        {
            _lastThreat.Remove(p);
        }

        if (_hits.Count > 0)
        {
            RpgCombatEventsMessageComposer events = new() { Hits = [.. _hits] };
            sends.Add(() => runtime.BroadcastAsync(events));
        }

        ImmutableArray<RpgCombatantVitals> vitals =
        [
            .. _vitalsChanged.Where(_combatants.ContainsKey).Select(id => Vitals(_combatants[id])),
        ];

        if (vitals.Length > 0)
        {
            RpgVitalsMessageComposer changed = new() { Combatants = vitals };
            sends.Add(() => runtime.BroadcastAsync(changed));
        }

        foreach (PlayerId player in _deaths)
        {
            RpgDeathMessageComposer death = new() { RespawnSeconds = config.AutoReleaseSeconds };
            sends.Add(() => runtime.SendToPlayerAsync(player, death));
        }

        foreach (int id in _selfChanged)
        {
            if (_combatants.TryGetValue(id, out Combatant? c) && c.PlayerId is { } player)
            {
                RpgSelfVitalsMessageComposer self = SelfVitals(c);
                sends.Add(() => runtime.SendToPlayerAsync(player, self));
            }
        }

        foreach ((PlayerId player, string code) in _results)
        {
            RpgCombatResultMessageComposer result = new() { Code = code };
            sends.Add(() => runtime.SendToPlayerAsync(player, result));
        }

        foreach (int id in _autoAttackChanged)
        {
            if (_combatants.TryGetValue(id, out Combatant? c) && c.PlayerId is { } player)
            {
                RpgAutoAttackStateMessageComposer state = new() { Active = c.AutoAttacking };
                sends.Add(() => runtime.SendToPlayerAsync(player, state));
            }
        }

        // Then what the modules queued (spec M1b): theirs come after combat's, in their order.
        foreach (IComposer composer in _broadcasts)
        {
            sends.Add(() => runtime.BroadcastAsync(composer));
        }

        foreach ((PlayerId player, IComposer composer) in _sends)
        {
            sends.Add(() => runtime.SendToPlayerAsync(player, composer));
        }

        ClearOutbox();

        // Different presences and the zone's stream: sent together. A send that fails (a presence
        // that just went away) is logged and costs nothing else: the tick has done its work.
        await Task.WhenAll(sends.Select(GuardedAsync));
    }

    private async Task GuardedAsync(Func<Task> send)
    {
        try
        {
            await send();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "A combat message of zone {ZoneId} was not sent.",
                runtime.ZoneId.Value
            );
        }
    }

    /// <summary>Nobody left to tell: what was pending is dropped, not sent to the next one in.</summary>
    private void ClearOutbox()
    {
        _hits.Clear();
        _vitalsChanged.Clear();
        _selfChanged.Clear();
        _deaths.Clear();
        _results.Clear();
        _autoAttackChanged.Clear();
        _broadcasts.Clear();
        _sends.Clear();
    }

    private static RpgCombatantVitals Vitals(Combatant c) =>
        new()
        {
            ObjectId = c.ObjectId,
            Health = c.Health,
            MaxHealth = c.MaxHealth,
        };

    private static RpgSelfVitalsMessageComposer SelfVitals(Combatant c) =>
        new()
        {
            Health = c.Health,
            MaxHealth = c.MaxHealth,
            Mana = c.Mana,
            MaxMana = c.MaxMana,
            InCombat = c.InCombat,
        };
}
