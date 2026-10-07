using System.Collections.Generic;
using System.Linq;
using Vortex.Primitives.Players;

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

    /// <summary>The players in the room, from their arrival step to their exit.</summary>
    public Dictionary<PlayerId, ZoneVisitor> Visitors { get; } = [];
}
