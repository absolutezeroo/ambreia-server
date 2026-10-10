using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vortex.Logging.Extensions;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Rpg.Characters;
using Vortex.Primitives.Rpg.Items.Enums;
using Vortex.Rpg.Items;

namespace Vortex.Rpg.Characters.Grains;

/// <summary>
/// What a zone's rules read of the character (spec Z1 §4.3): its level and what it owns, bag and
/// equipment — a parcel is not claimed, so not owned yet. The zone reads them on entry and is told
/// each change after; every reading carries a sequence that only grows, seeded at activation from
/// the process's monotonic clock — a wall clock stepped back would make the zone drop every push —
/// so the zone drops one that arrives after a newer one.
/// </summary>
internal sealed partial class PlayerCharacterGrain
{
    // ponytail: monotonic for one process; the rooms holding these parts live in the same silo.
    // Several silos would need a sequence kept with the character instead.
    private long _zoneFactsSequence = Stopwatch.GetTimestamp();

    /// <summary>The last facts the zone was given or read: an operation that changes nothing in
    /// them (a move, an equip) tells it nothing.</summary>
    private CharacterZoneFacts? _zoneFactsTold;

    public async Task<CharacterZoneFacts?> GetZoneFactsAsync()
    {
        CharacterInventory? inventory = await InventoryAsync();

        if (_character is not { } c || inventory is null)
        {
            return null;
        }

        return _zoneFactsTold = Facts(c.Level, inventory);
    }

    private CharacterZoneFacts Facts(int level, CharacterInventory inventory) =>
        new()
        {
            Sequence = ++_zoneFactsSequence,
            Level = level,
            Items = inventory
                .Live.Where(i => i.Container is ItemContainer.Bag or ItemContainer.Equipment)
                .GroupBy(i => i.DefinitionId)
                .ToImmutableDictionary(g => g.Key, g => g.Sum(i => i.Quantity)),
        };

    /// <summary>
    /// The level or what is owned may have changed: the zone the player stands in is told, through
    /// the presence. From memory only — no read, nothing that can fail after the change is written;
    /// an inventory not loaded yet has never been read by a zone either. Never awaited, as the
    /// stats: the zone may be waiting on this very grain.
    /// </summary>
    private void TellTheZoneFacts()
    {
        if (_character is not { } c || _inventory is not { } inventory)
        {
            return;
        }

        CharacterZoneFacts facts = Facts(c.Level, inventory);

        if (_zoneFactsTold is { } told && Same(told, facts))
        {
            return;
        }

        _zoneFactsTold = facts;

        try
        {
            grainFactory
                .GetPlayerPresenceGrain(_playerId)
                .OnZoneFactsChangedAsync(facts, CancellationToken.None)
                .LogAndForget(
                    logger,
                    "Passing the zone facts of player {PlayerId} to its zone",
                    _playerId.Value
                );
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Zone facts of player {PlayerId} not sent", _playerId.Value);
        }
    }

    /// <summary>The same, loading the bag first if this activation has not: a failed read is
    /// logged, and only then is nothing told.</summary>
    private async Task TellTheZoneFactsLoadingAsync()
    {
        try
        {
            await InventoryAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Zone facts of player {PlayerId} not sent: the bag could not be read",
                _playerId.Value
            );

            return;
        }

        TellTheZoneFacts();
    }

    private static bool Same(CharacterZoneFacts a, CharacterZoneFacts b) =>
        a.Level == b.Level
        && a.Items.Count == b.Items.Count
        && a.Items.All(i => b.Items.TryGetValue(i.Key, out int n) && n == i.Value);
}
