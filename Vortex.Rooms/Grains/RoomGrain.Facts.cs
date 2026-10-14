using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Events.Zone;
using Vortex.Primitives.Rpg.Quests;
using Vortex.Primitives.World.Layers;
using Vortex.Primitives.World.Rules;
using Vortex.Rpg.Zones.Rules;

namespace Vortex.Rooms.Grains;

/// <summary>What the zone knows of each player, part by part (spec Z1 §4.3).</summary>
public sealed partial class RoomGrain
{
    /// <summary>
    /// Server side only: the player's presence passes on a part of what the zone knows of them.
    /// A part older than the one held arrived late, past a newer one: it is dropped. Against the
    /// part held, the module that declared it says what the change sets off (spec Z1 §5.2–5.3); a
    /// first part has nothing to compare with.
    /// </summary>
    public async Task UpdatePlayerFactsAsync(PlayerId player, ZoneFacts facts)
    {
        // A relay that arrives after the player left must not bring them back.
        if (
            player.Value <= 0
            || _state.ZoneRuntime is not { } zone
            || !_state.AvatarsByPlayerId.ContainsKey(player)
        )
        {
            return;
        }

        Type type = facts.GetType();
        Dictionary<Type, ZoneFacts> parts = PartsOf(zone.Facts, player);
        parts.TryGetValue(type, out ZoneFacts? held);

        if (held is not null && held.Sequence >= facts.Sequence)
        {
            return;
        }

        parts[type] = facts;

        // The NPC markers follow the player's own quests (spec M2a §3.3).
        if (facts is QuestZoneFacts)
        {
            await DialogueModule.SendMarkersAsync(player);
        }

        if (held is null || _zoneRules.SourceOf(type) is not { } source)
        {
            return;
        }

        foreach (ZoneTriggerSignal signal in SignalsOf(source, held, facts))
        {
            await RaiseZoneTriggerAsync(player, signal.Type, signal.Target);
        }
    }

    private ZoneTriggerSignal[] SignalsOf(
        IZoneFactsSource source,
        ZoneFacts before,
        ZoneFacts after
    )
    {
        try
        {
            return [.. source.Changes(before, after)];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "{Source} could not say what a change sets off in room {RoomId}.",
                source.GetType().Name,
                RoomId.Value
            );

            return [];
        }
    }

    /// <summary>The zone's triggers of this type and target run for the player (spec Z1 §5).</summary>
    internal Task RaiseZoneTriggerAsync(PlayerId player, ZoneTriggerType type, string target) =>
        EventModule.PublishAsync(
            new ZoneTriggerRaisedEvent
            {
                RoomId = RoomId,
                PlayerId = player,
                Type = type,
                Target = target,
            },
            CancellationToken.None
        );

    /// <summary>
    /// Every declared part of the player, read at once (spec Z1 §4.3). A source that fails is
    /// logged and leaves its part out: the player still enters.
    /// </summary>
    internal async Task<ZoneFacts[]> ReadFactsAsync(PlayerId player)
    {
        IReadOnlyCollection<IZoneFactsSource> sources = _zoneRules.FactsSources;

        if (sources.Count == 0)
        {
            return [];
        }

        ZoneFacts?[] read = await Task.WhenAll(sources.Select(s => ReadPartAsync(s, player)));

        return [.. read.OfType<ZoneFacts>()];
    }

    private async Task<ZoneFacts?> ReadPartAsync(IZoneFactsSource source, PlayerId player)
    {
        try
        {
            return await source.ReadAsync(player);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "{Source} could not be read for player {PlayerId} in room {RoomId}.",
                source.GetType().Name,
                player.Value,
                RoomId.Value
            );

            return null;
        }
    }

    internal static Dictionary<Type, ZoneFacts> PartsOf(
        Dictionary<PlayerId, Dictionary<Type, ZoneFacts>> facts,
        PlayerId player
    )
    {
        if (!facts.TryGetValue(player, out Dictionary<Type, ZoneFacts>? parts))
        {
            facts[player] = parts = [];
        }

        return parts;
    }
}
