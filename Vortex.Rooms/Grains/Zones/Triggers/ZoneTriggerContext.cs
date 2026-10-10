using System;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Object.Avatars;
using Vortex.Primitives.Rpg.Characters;
using Vortex.Rooms.Grains.Zones.Runtime;
using Vortex.Rpg.Zones.Rules;

namespace Vortex.Rooms.Grains.Zones.Triggers;

/// <summary>
/// What a condition reads and an action acts on: the room, its zone, the player who set it off
/// (none for a clock), the server's local time, and what the zone knows of the player's character.
/// </summary>
public sealed record ZoneTriggerContext(
    RoomGrain Room,
    ZoneRuntimeState Zone,
    PlayerId PlayerId,
    IRoomPlayer? Player,
    DateTimeOffset LocalNow,
    CharacterZoneFacts? Facts
)
{
    /// <summary>The same, as a module's rule sees it: no room, the zone's runtime instead.</summary>
    public ZoneRuleContext ToRuleContext() =>
        new(PlayerId, Facts, LocalNow, Room.CombatRuntime, Room.Simulation);
}
