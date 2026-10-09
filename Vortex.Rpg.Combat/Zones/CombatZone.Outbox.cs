using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vortex.Primitives.Players;
using Vortex.Protocol.Messages.Outgoing.Rpg.Combat;
using Vortex.Rpg.Combat.Combatants;

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
    private long _nextRegenAtMs;

    private async Task FlushAsync()
    {
        List<Task> sends = [];

        if (_hits.Count > 0)
        {
            sends.Add(
                runtime.BroadcastAsync(new RpgCombatEventsMessageComposer { Hits = [.. _hits] })
            );
        }

        ImmutableArray<RpgCombatantVitals> vitals =
        [
            .. _vitalsChanged.Where(_combatants.ContainsKey).Select(id => Vitals(_combatants[id])),
        ];

        if (vitals.Length > 0)
        {
            sends.Add(runtime.BroadcastAsync(new RpgVitalsMessageComposer { Combatants = vitals }));
        }

        foreach (PlayerId player in _deaths)
        {
            sends.Add(
                runtime.SendToPlayerAsync(
                    player,
                    new RpgDeathMessageComposer { RespawnSeconds = config.GhostSeconds }
                )
            );
        }

        foreach (int id in _selfChanged)
        {
            if (_combatants.TryGetValue(id, out Combatant? c) && c.PlayerId is { } player)
            {
                sends.Add(runtime.SendToPlayerAsync(player, SelfVitals(c)));
            }
        }

        foreach ((PlayerId player, string code) in _results)
        {
            sends.Add(
                runtime.SendToPlayerAsync(
                    player,
                    new RpgCombatResultMessageComposer { Code = code }
                )
            );
        }

        _hits.Clear();
        _vitalsChanged.Clear();
        _selfChanged.Clear();
        _deaths.Clear();
        _results.Clear();

        // Different presences and the zone's stream: sent together. A send that fails (a presence
        // that just went away) is logged and costs nothing else: the tick has done its work.
        await Task.WhenAll(sends.Select(GuardedAsync));
    }

    private async Task GuardedAsync(Task send)
    {
        try
        {
            await send;
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
