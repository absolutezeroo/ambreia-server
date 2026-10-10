using System;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rpg.Characters;

namespace Vortex.Rpg.Zones.Rules;

/// <summary>
/// What a module's condition reads and its action acts on (spec Z1 §4.2): the player who set it off
/// (<see cref="PlayerId.Invalid"/> for a clock or a spawn point), what the zone knows of their
/// character (null without one), the server's local time, and the zone itself.
/// </summary>
public sealed record ZoneRuleContext(
    PlayerId PlayerId,
    CharacterZoneFacts? Facts,
    DateTimeOffset LocalNow,
    IZoneRuntime Runtime,
    IZoneSimulation Simulation
);
