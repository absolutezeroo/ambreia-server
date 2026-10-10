using System;
using System.Collections.Generic;
using System.Linq;
using Vortex.Primitives.Players;
using Vortex.Primitives.World;
using Vortex.Primitives.World.Layers;
using Vortex.Primitives.World.Rules;
using Vortex.Rooms.Grains.Zones.Compilation;

namespace Vortex.Rooms.Grains.Zones.Runtime;

/// <summary>The live side of a published zone: its compiled document and who is expected where.</summary>
public sealed class ZoneRuntimeState(CompiledZone compiled)
{
    public CompiledZone Compiled { get; } = compiled;

    /// <summary>Each "door" furni by room object id, and whether it stands open (state 1 at load).</summary>
    public Dictionary<int, bool> Doors { get; } =
        Enumerable
            .Range(1, compiled.Document.Decor.Count)
            .Where(id => compiled.FloorDecor(id)?.Interaction == "door")
            .ToDictionary(id => id, id => compiled.FloorDecor(id)!.State != 0);

    /// <summary>Whether the furni can be walked on, when the zone decides rather than its logic.</summary>
    public bool? Walkable(int objectId) =>
        Doors.TryGetValue(objectId, out bool open)
            ? open
            : Compiled.FloorDecor(objectId)?.Collision?.Walkable;

    /// <summary>
    /// Whether the furni is a seat, when the zone decides rather than its logic. A door never is:
    /// a seat is a legal destination even when it cannot be walked on, so a closed door on a throne
    /// would let anyone sit their way through it.
    /// </summary>
    public bool? Seat(int objectId) =>
        Doors.ContainsKey(objectId) ? false : Compiled.FloorDecor(objectId)?.Collision?.Seat;

    /// <summary>Whether the furni is a bed, when the zone decides: a door never is.</summary>
    public bool? Bed(int objectId) => Doors.ContainsKey(objectId) ? false : null;

    /// <summary>The entry each expected player arrives on, and when the expectation lapses.</summary>
    public Dictionary<PlayerId, ZoneExpectedArrival> ExpectedArrivals { get; } = [];

    private readonly HashSet<string> _inactiveAreas = [];

    /// <summary>
    /// Per cell, worked out on first use and kept until an area is switched: what a step, a bubble
    /// or (with the RPG) every attack asks, without filtering or allocating each time.
    /// </summary>
    private readonly IReadOnlyList<ZoneArea>?[] _activeAreasAt = new IReadOnlyList<ZoneArea>?[
        compiled.Width * compiled.Length
    ];
    private readonly ZoneCellRules?[] _rulesAt = new ZoneCellRules?[
        compiled.Width * compiled.Length
    ];

    /// <summary>Areas switched off by an action: nobody enters or leaves them (plan D13).</summary>
    public IReadOnlySet<string> InactiveAreas => _inactiveAreas;

    /// <summary>Switches an area on or off (ActivateZone / DeactivateZone); the cells forget what they knew.</summary>
    public void SetAreaActive(string areaId, bool active)
    {
        if (active ? _inactiveAreas.Remove(areaId) : _inactiveAreas.Add(areaId))
        {
            Array.Clear(_activeAreasAt);
            Array.Clear(_rulesAt);
        }
    }

    /// <summary>The areas covering (x, y) that are switched on, highest priority first.</summary>
    public IReadOnlyList<ZoneArea> ActiveAreasAt(int x, int y)
    {
        if (_inactiveAreas.Count == 0 || !Inside(x, y))
        {
            return Compiled.AreasAt(x, y);
        }

        int cell = y * Compiled.Width + x;

        return _activeAreasAt[cell] ??= [
            .. Compiled.AreasAt(x, y).Where(a => !_inactiveAreas.Contains(a.Id)),
        ];
    }

    /// <summary>What (x, y) allows (plan D24), worked out once per cell and area switch.</summary>
    public ZoneCellRules RulesAt(int x, int y) =>
        Inside(x, y)
            ? _rulesAt[y * Compiled.Width + x] ??= ZoneRuleBook.At(this, x, y)
            : ZoneRuleBook.At(this, x, y);

    private bool Inside(int x, int y) =>
        x >= 0 && y >= 0 && x < Compiled.Width && y < Compiled.Length;

    /// <summary>When each passage and trigger works again, per player; outlives the player's visit.</summary>
    public ZoneCooldowns Cooldowns { get; } = new();

    /// <summary>When each Timer trigger is next due, in epoch milliseconds.</summary>
    public Dictionary<string, long> TimerNextAtMs { get; } = [];

    /// <summary>The players in the room, from their arrival step to their exit.</summary>
    public Dictionary<PlayerId, ZoneVisitor> Visitors { get; } = [];

    /// <summary>
    /// What the zone knows of each player, part by part as the modules declare them (spec Z1 §4.3):
    /// read on entry, a part replaced by a newer one when its module pushes it, all forgotten on
    /// exit. The zone never looks inside a part.
    /// </summary>
    public Dictionary<PlayerId, Dictionary<Type, ZoneFacts>> Facts { get; } = [];

    /// <summary>What the zone knows of nobody.</summary>
    public static IReadOnlyDictionary<Type, ZoneFacts> NoFacts { get; } =
        new Dictionary<Type, ZoneFacts>();

    /// <summary>The parts the zone holds for <paramref name="player"/>, none if it holds nothing.</summary>
    public IReadOnlyDictionary<Type, ZoneFacts> FactsOf(PlayerId player) =>
        Facts.TryGetValue(player, out Dictionary<Type, ZoneFacts>? parts) ? parts : NoFacts;
}
