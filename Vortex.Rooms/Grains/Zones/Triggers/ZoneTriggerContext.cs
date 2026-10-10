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
/// A class, not a record: it caches the module's view, which a copy must not carry over.
/// </summary>
public sealed class ZoneTriggerContext(
    RoomGrain room,
    ZoneRuntimeState zone,
    PlayerId playerId,
    IRoomPlayer? player,
    DateTimeOffset localNow,
    IReadOnlyDictionary<Type, ZoneFacts> facts
)
{
    private ZoneRuleContext? _rule;

    public RoomGrain Room { get; } = room;

    public ZoneRuntimeState Zone { get; } = zone;

    public PlayerId PlayerId { get; } = playerId;

    public IRoomPlayer? Player { get; } = player;

    public DateTimeOffset LocalNow { get; } = localNow;

    public IReadOnlyDictionary<Type, ZoneFacts> Facts { get; } = facts;

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
