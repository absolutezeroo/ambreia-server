using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vortex.Primitives.Players;
using Vortex.Primitives.World.Rules;
using Vortex.Rpg.Zones.Rules;

namespace Vortex.Rooms.Grains;

/// <summary>What the zone knows of each player, part by part (spec Z1 §4.3).</summary>
public sealed partial class RoomGrain
{
    /// <summary>
    /// Server side only: the player's presence passes on a part of what the zone knows of them.
    /// A part older than the one held arrived late, past a newer one: it is dropped.
    /// </summary>
    public Task UpdatePlayerFactsAsync(PlayerId player, ZoneFacts facts)
    {
        // A relay that arrives after the player left must not bring them back.
        if (
            player.Value <= 0
            || _state.ZoneRuntime is not { } zone
            || !_state.AvatarsByPlayerId.ContainsKey(player)
        )
        {
            return Task.CompletedTask;
        }

        Dictionary<Type, ZoneFacts> parts = PartsOf(zone.Facts, player);

        if (
            parts.TryGetValue(facts.GetType(), out ZoneFacts? held)
            && held.Sequence >= facts.Sequence
        )
        {
            return Task.CompletedTask;
        }

        parts[facts.GetType()] = facts;

        return Task.CompletedTask;
    }

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
