using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Vortex.Primitives.Players;
using Vortex.Primitives.World.Rules;

namespace Vortex.Rpg.Zones.Rules;

/// <summary>
/// A module's part of what a zone knows of a player (spec Z1 §4.3), declared with
/// <see cref="ZoneRuleServices.AddZoneFactsSource{T}"/>: how the zone reads it when the player
/// enters, and which triggers a change of it sets off. The zone keeps the part and never looks
/// inside: only the module's own conditions do. A singleton shared by every zone: no state.
/// </summary>
public interface IZoneFactsSource
{
    /// <summary>The <see cref="ZoneFacts"/> record this source reads.</summary>
    public Type FactsType { get; }

    /// <summary>
    /// The player's part as it is now; null when there is none (no character). The first part the
    /// zone holds has nothing to compare with and sets off nothing: a source whose first change
    /// must fire (a first quest accepted) answers an empty part rather than null.
    /// </summary>
    public Task<ZoneFacts?> ReadAsync(PlayerId player);

    /// <summary>What a change from <paramref name="before"/> to <paramref name="after"/> sets off,
    /// both of <see cref="FactsType"/>.</summary>
    public IEnumerable<ZoneTriggerSignal> Changes(ZoneFacts before, ZoneFacts after);
}
