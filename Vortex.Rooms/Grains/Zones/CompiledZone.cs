using System.Collections.Generic;
using System.Linq;
using Vortex.Primitives.World;
using Vortex.Primitives.World.Layers;

namespace Vortex.Rooms.Grains.Zones;

/// <summary>
/// A published zone document read once for the room's lifetime: what each cell is part of
/// (areas by priority, passage), the entries, the triggers by type, and the decor's object ids.
/// Immutable: a republication builds a new one with the new activation.
/// </summary>
public sealed class CompiledZone
{
    private readonly IReadOnlyList<ZoneArea>[] _areasAt;
    private readonly ZonePassage?[] _passageAt;
    private readonly IReadOnlyDictionary<string, int> _objectIds;
    private readonly IReadOnlyDictionary<int, string> _decorIds;

    internal CompiledZone(
        ZoneDocument document,
        IReadOnlyList<ZoneArea>[] areasAt,
        ZonePassage?[] passageAt,
        IReadOnlyDictionary<string, int> objectIds,
        IReadOnlyDictionary<string, IReadOnlySet<int>> areaTiles
    )
    {
        Document = document;
        _areasAt = areasAt;
        _passageAt = passageAt;
        _objectIds = objectIds;
        _decorIds = objectIds.ToDictionary(p => p.Value, p => p.Key);
        AreaTiles = areaTiles;
        Entries = document.Entries.GroupBy(e => e.Id).ToDictionary(g => g.Key, g => g.First());
        DefaultArrival = document.Entries.FirstOrDefault(e => e.Kind == ZoneEntryKinds.Arrival);
        Triggers = document.Triggers.ToLookup(t => t.Type);
        Areas = document.Zones.GroupBy(a => a.Id).ToDictionary(g => g.Key, g => g.First());
    }

    public ZoneDocument Document { get; }

    public int Width => Document.Floor.Width;

    public int Length => Document.Floor.Length;

    public IReadOnlyDictionary<string, ZoneEntry> Entries { get; }

    /// <summary>Where a player arrives when nothing names an entry: the first "arrival".</summary>
    public ZoneEntry? DefaultArrival { get; }

    public ILookup<ZoneTriggerType, ZoneTrigger> Triggers { get; }

    public IReadOnlyDictionary<string, ZoneArea> Areas { get; }

    /// <summary>The cells (y * width + x) of each area.</summary>
    public IReadOnlyDictionary<string, IReadOnlySet<int>> AreaTiles { get; }

    /// <summary>The areas covering (x, y), highest priority first; at equal priority the later one.</summary>
    public IReadOnlyList<ZoneArea> AreasAt(int x, int y) =>
        Inside(x, y) ? _areasAt[y * Width + x] : [];

    public ZonePassage? PassageAt(int x, int y) => Inside(x, y) ? _passageAt[y * Width + x] : null;

    /// <summary>The room object id of the decor object with this document id.</summary>
    public int? DecorObjectId(string decorId) =>
        _objectIds.TryGetValue(decorId, out int id) ? id : null;

    public string? DecorId(int objectId) => _decorIds.GetValueOrDefault(objectId);

    private bool Inside(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Length;
}
