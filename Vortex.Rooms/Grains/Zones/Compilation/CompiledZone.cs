using System.Collections.Generic;
using System.Linq;
using Vortex.Primitives.World;
using Vortex.Primitives.World.Layers;

namespace Vortex.Rooms.Grains.Zones.Compilation;

/// <summary>
/// A published zone document read once for the room's lifetime: what each cell is part of
/// (areas by priority, passage), the entries, the active triggers by type and target, the clocks,
/// and the decor's object ids.
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
        _triggersByTarget = document
            .Triggers.Where(t => t.Active)
            .ToLookup(t => (t.Type, t.Target ?? string.Empty));
        Timers =
        [
            .. document
                .Triggers.Where(t => t.Active && t.Type == ZoneTriggerType.Timer)
                .Select(t => (Trigger: t, Seconds: int.TryParse(t.Target, out int s) ? s : 0))
                .Where(t => t.Seconds > 0),
        ];
        Areas = document.Zones.GroupBy(a => a.Id).ToDictionary(g => g.Key, g => g.First());
        Interactives =
        [
            .. document
                .Decor.Select((decor, index) => (Decor: decor, ObjectId: index + 1))
                .Select(x =>
                    x.Decor.Interaction is { Length: > 0 } kind
                        ? new ZoneClickable(x.ObjectId, kind)
                    : TriggersFor(ZoneTriggerType.Interact, x.Decor.Id).Any()
                        ? new ZoneClickable(x.ObjectId, InteractTrigger)
                    : null
                )
                .OfType<ZoneClickable>(),
        ];
    }

    /// <summary>What a decor aimed at by an Interact trigger, and with no interaction of its own,
    /// does when clicked: it fires the trigger (plan F3b, décision 1).</summary>
    public const string InteractTrigger = "interact";

    /// <summary>What a player can click in this zone (spec F3b §5): every floor decor with an
    /// interaction, or listened to by an Interact trigger, by room object id. The rest is frozen.</summary>
    public IReadOnlyList<ZoneClickable> Interactives { get; }

    private readonly ILookup<(ZoneTriggerType Type, string Target), ZoneTrigger> _triggersByTarget;

    /// <summary>
    /// The active triggers of this type aimed at this target, in document order: what an area
    /// event or a furni use looks up, without scanning every trigger each time.
    /// </summary>
    public IEnumerable<ZoneTrigger> TriggersFor(ZoneTriggerType type, string target) =>
        _triggersByTarget[(type, target)];

    /// <summary>The active clock triggers and their period in seconds; a bad period is left out.</summary>
    public IReadOnlyList<(ZoneTrigger Trigger, int Seconds)> Timers { get; }

    public ZoneDocument Document { get; }

    public int Width => Document.Floor.Width;

    public int Length => Document.Floor.Length;

    public IReadOnlyDictionary<string, ZoneEntry> Entries { get; }

    /// <summary>Where a player arrives when nothing names an entry: the first "arrival".</summary>
    public ZoneEntry? DefaultArrival { get; }

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

    /// <summary>The floor decor object behind a room object id, or null (wall decor, not decor).</summary>
    public ZoneDecorObject? FloorDecor(int objectId) =>
        objectId >= 1 && objectId <= Document.Decor.Count ? Document.Decor[objectId - 1] : null;

    private bool Inside(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Length;
}
