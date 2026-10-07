using System.Collections.Generic;
using System.Linq;
using Vortex.Primitives.Players;
using Vortex.Primitives.World.Layers;

namespace Vortex.Rooms.Grains.Zones;

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
    public Dictionary<PlayerId, (string EntryId, long ExpiresAtMs)> ExpectedArrivals { get; } = [];

    /// <summary>Areas switched off by an action: nobody enters or leaves them (plan D13).</summary>
    public HashSet<string> InactiveAreas { get; } = [];

    /// <summary>The areas covering (x, y) that are switched on, highest priority first.</summary>
    public IReadOnlyList<ZoneArea> ActiveAreasAt(int x, int y) =>
        InactiveAreas.Count == 0
            ? Compiled.AreasAt(x, y)
            : [.. Compiled.AreasAt(x, y).Where(a => !InactiveAreas.Contains(a.Id))];

    /// <summary>When each trigger fires again for everyone (the clock's, and those set off by nobody).</summary>
    public Dictionary<string, long> TriggerReadyAtMs { get; } = [];

    /// <summary>When each Timer trigger is next due, in epoch milliseconds.</summary>
    public Dictionary<string, long> TimerNextAtMs { get; } = [];

    /// <summary>The players in the room, from their arrival step to their exit.</summary>
    public Dictionary<PlayerId, ZoneVisitor> Visitors { get; } = [];
}
