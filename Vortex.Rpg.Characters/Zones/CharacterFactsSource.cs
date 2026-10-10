using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Orleans;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rpg.Characters;
using Vortex.Primitives.World.Rules;
using Vortex.Rpg.Zones.Rules;

namespace Vortex.Rpg.Characters.Zones;

/// <summary>
/// The character's part of what a zone knows of a player (spec Z1 §4.3): read from the character
/// when the player enters; afterwards the character pushes it on each change.
/// </summary>
public sealed class CharacterFactsSource(IGrainFactory grains) : IZoneFactsSource
{
    public Type FactsType => typeof(CharacterZoneFacts);

    public async Task<ZoneFacts?> ReadAsync(PlayerId player) =>
        await grains.GetPlayerCharacterGrain(player).GetZoneFactsAsync();

    /// <summary>Nothing yet: what a level or an item sets off comes with the triggers (spec Z1 §5).</summary>
    public IEnumerable<ZoneTriggerSignal> Changes(ZoneFacts before, ZoneFacts after) => [];
}
