using System.Collections.Immutable;
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
/// each change after.
/// </summary>
internal sealed partial class PlayerCharacterGrain
{
    public async Task<CharacterZoneFacts?> GetZoneFactsAsync()
    {
        CharacterInventory? inventory = await InventoryAsync();

        return _character is { } c && inventory is not null ? Facts(c.Level, inventory) : null;
    }

    private static CharacterZoneFacts Facts(int level, CharacterInventory inventory) =>
        new()
        {
            Level = level,
            Items = inventory
                .Live.Where(i => i.Container is ItemContainer.Bag or ItemContainer.Equipment)
                .GroupBy(i => i.DefinitionId)
                .ToImmutableDictionary(g => g.Key, g => g.Sum(i => i.Quantity)),
        };

    /// <summary>
    /// The level or what is owned changed: the zone the player stands in is told, through the
    /// presence. Never awaited, as the stats: the zone may be waiting on this very grain.
    /// </summary>
    private async Task TellTheZoneFactsAsync()
    {
        if (await GetZoneFactsAsync() is not { } facts)
        {
            return;
        }

        grainFactory
            .GetPlayerPresenceGrain(_playerId)
            .OnCharacterFactsChangedAsync(facts, CancellationToken.None)
            .LogAndForget(
                logger,
                "Passing the zone facts of player {PlayerId} to its zone",
                _playerId.Value
            );
    }
}
