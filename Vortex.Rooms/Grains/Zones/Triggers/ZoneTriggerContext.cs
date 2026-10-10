using System;
using System.Collections.Generic;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Object.Avatars;
using Vortex.Primitives.World.Rules;
using Vortex.Rooms.Grains.Zones.Runtime;
using Vortex.Rpg.Zones.Rules;

namespace Vortex.Rooms.Grains.Zones.Triggers;

/// <summary>
/// What a condition reads and an action acts on: the room, its zone, the player who set it off
/// (none for a clock), the server's local time, and the parts of what the zone knows of the player.
/// </summary>
public sealed record ZoneTriggerContext(
    RoomGrain Room,
    ZoneRuntimeState Zone,
    PlayerId PlayerId,
    IRoomPlayer? Player,
    DateTimeOffset LocalNow,
    IReadOnlyDictionary<Type, ZoneFacts> Facts
)
{
    private ZoneRuleContext? _rule;

    /// <summary>
    /// The same, as a module's rule sees it: no room, the zone's runtime instead; nobody (a clock,
    /// player 0) is <see cref="PlayerId.Invalid"/>. Made once per check, however many rules ask.
    /// </summary>
    public ZoneRuleContext ToRuleContext() =>
        _rule ??= new(
            PlayerId.Value > 0 ? PlayerId : PlayerId.Invalid,
            Facts,
            LocalNow,
            Room.CombatRuntime,
            Room.Simulation
        );
}
