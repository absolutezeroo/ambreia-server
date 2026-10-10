using System;
using System.Collections.Generic;
using Vortex.Primitives.Players;
using Vortex.Primitives.World.Rules;

namespace Vortex.Rpg.Zones.Rules;

/// <summary>
/// What a module's condition reads and its action acts on (spec Z1 §4.2): the player who set it off
/// (<see cref="PlayerId.Invalid"/> for a clock or a spawn point), the parts of what the zone knows
/// of them by type (empty for nobody), the server's local time, and the zone itself.
/// </summary>
public sealed record ZoneRuleContext(
    PlayerId PlayerId,
    IReadOnlyDictionary<Type, ZoneFacts> Facts,
    DateTimeOffset LocalNow,
    IZoneRuntime Runtime,
    IZoneSimulation Simulation
)
{
    /// <summary>The part a module declared, or null when the zone has none for this player.</summary>
    public T? FactsOf<T>()
        where T : ZoneFacts => Facts.TryGetValue(typeof(T), out ZoneFacts? part) ? (T)part : null;
}
