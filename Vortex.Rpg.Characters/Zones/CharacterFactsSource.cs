using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Orleans;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rpg.Characters;
using Vortex.Primitives.World.Layers;
using Vortex.Primitives.World.Rules;
using Vortex.Rpg.Zones.Rules;

namespace Vortex.Rpg.Characters.Zones;

/// <summary>
/// The character's part of what a zone knows of a player (spec Z1 §4.3): read from the character
/// when the player enters; afterwards the character pushes it on each change, and this says what
/// the change sets off — each level crossed, each item whose count went up (§5.2–5.3).
/// </summary>
public sealed class CharacterFactsSource(IGrainFactory grains) : IZoneFactsSource
{
    public Type FactsType => typeof(CharacterZoneFacts);

    public IReadOnlyCollection<ZoneTriggerType> Raises { get; } =
    [ZoneTriggerType.PlayerLevel, ZoneTriggerType.ItemOwned];

    public async Task<ZoneFacts?> ReadAsync(PlayerId player) =>
        await grains.GetPlayerCharacterGrain(player).GetZoneFactsAsync();

    public IEnumerable<ZoneTriggerSignal> Changes(ZoneFacts before, ZoneFacts after)
    {
        if (before is not CharacterZoneFacts was || after is not CharacterZoneFacts now)
        {
            yield break;
        }

        for (int level = was.Level + 1; level <= now.Level; level++)
        {
            yield return new ZoneTriggerSignal(
                ZoneTriggerType.PlayerLevel,
                level.ToString(CultureInfo.InvariantCulture)
            );
        }

        foreach ((string item, int count) in now.Items)
        {
            if (count > was.Items.GetValueOrDefault(item))
            {
                yield return new ZoneTriggerSignal(ZoneTriggerType.ItemOwned, item);
            }
        }
    }
}
