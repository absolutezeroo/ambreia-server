using System.Collections.Generic;
using System.Linq;
using Vortex.Primitives.World;
using Vortex.Primitives.World.Geometry;
using Vortex.Primitives.World.Layers;
using Vortex.Primitives.World.Rules;

namespace Vortex.World;

/// <summary>
/// The checks that follow what the server does with a zone at run time (plan phase 6): a trigger
/// aims at what its type listens to, an action and a teleporter name what exists, RPG content is
/// flagged inert, triggers teleporting into each other and Safe/PvP overlaps warn.
/// </summary>
internal static class ZoneRuntimeRules
{
    /// <summary>The interactions ZoneInteractionModule knows; "" or null leaves the furni's logic.</summary>
    private static readonly HashSet<string> Interactions =
    [
        "door",
        "lever",
        "sign",
        "teleport",
        "sit",
        "use",
    ];

    private static readonly HashSet<ZoneTriggerType> RpgTriggers =
    [
        ZoneTriggerType.MonsterKilled,
        ZoneTriggerType.QuestState,
        ZoneTriggerType.PlayerLevel,
        ZoneTriggerType.ItemOwned,
    ];

    public static void Check(ZoneDocument d, IReadOnlySet<string> knownZones, List<ZoneFinding> f)
    {
        HashSet<string> areas = [.. d.Zones.Select(z => z.Id)];
        Dictionary<string, ZoneDecorObject> decor = d
            .Decor.GroupBy(o => o.Id)
            .ToDictionary(g => g.Key, g => g.First());
        HashSet<string> entries = [.. d.Entries.Select(e => e.Id)];
        HashSet<string> passages = [.. d.Passages.Select(p => p.Id)];
        string here = d.Settings.Id;

        foreach (ZoneTrigger t in d.Triggers)
        {
            CheckTarget(t, areas, decor, f);

            foreach (ZoneCondition c in t.Conditions)
            {
                CheckCondition(t.Id, c, areas, f);
            }

            foreach (ZoneAction a in t.Actions)
            {
                CheckAction(t.Id, a, areas, decor, entries, here, knownZones, f);
            }
        }

        foreach (ZonePassage p in d.Passages)
        {
            if (p.DestinationZone == here && !entries.Contains(p.DestinationEntry))
            {
                f.Add(
                    Error(
                        "BrokenReference",
                        p.Id,
                        $"Entry '{p.DestinationEntry}' does not exist here."
                    )
                );
            }

            foreach (ZoneCondition c in p.Conditions)
            {
                CheckCondition(p.Id, c, areas, f);
            }
        }

        foreach (ZoneDecorObject o in d.Decor.Where(o => !string.IsNullOrEmpty(o.Interaction)))
        {
            CheckInteraction(o, entries, passages, f);
        }

        CheckAreas(d, f);
        CheckLoops(d, f);
        CheckOverlaps(d, f);
    }

    private static void CheckTarget(
        ZoneTrigger t,
        HashSet<string> areas,
        Dictionary<string, ZoneDecorObject> decor,
        List<ZoneFinding> f
    )
    {
        switch (t.Type)
        {
            case ZoneTriggerType.Timer:
                if (!int.TryParse(t.Target, out int seconds) || seconds <= 0)
                {
                    f.Add(
                        Error(
                            "BadTimer",
                            t.Id,
                            $"A timer's target is its period in seconds, not '{t.Target}'."
                        )
                    );
                }

                break;

            // A target that names nothing at all is the BrokenReference of ZoneValidator.
            case ZoneTriggerType.EnterZone
            or ZoneTriggerType.LeaveZone when t.Target is null || decor.ContainsKey(t.Target):
                f.Add(
                    Error(
                        "WrongTarget",
                        t.Id,
                        $"{t.Type} listens to an area; '{t.Target}' is not one."
                    )
                );

                break;

            case ZoneTriggerType.Interact when t.Target is null || areas.Contains(t.Target):
                f.Add(
                    Error(
                        "WrongTarget",
                        t.Id,
                        $"Interact listens to a furni; '{t.Target}' is not one."
                    )
                );

                break;

            case var type when RpgTriggers.Contains(type):
                f.Add(Inert(t.Id, $"trigger {type}"));

                break;
        }
    }

    private static void CheckCondition(
        string owner,
        ZoneCondition c,
        HashSet<string> areas,
        List<ZoneFinding> f
    )
    {
        switch (c)
        {
            case ZoneActiveCondition active when !areas.Contains(active.Zone):
                f.Add(Error("BrokenReference", owner, $"Area '{active.Zone}' does not exist."));

                break;

            case PlayerLevelCondition
            or QuestStateCondition
            or HasItemCondition
            or FactionCondition:
                f.Add(Inert(owner, $"condition {c.GetType().Name}, always false"));

                break;
        }
    }

    private static void CheckAction(
        string owner,
        ZoneAction a,
        HashSet<string> areas,
        Dictionary<string, ZoneDecorObject> decor,
        HashSet<string> entries,
        string here,
        IReadOnlySet<string> knownZones,
        List<ZoneFinding> f
    )
    {
        switch (a)
        {
            case ActivateZoneAction { Zone: var area } when !areas.Contains(area):
            case DeactivateZoneAction { Zone: var deactivated } when !areas.Contains(deactivated):
                f.Add(
                    Error("BrokenReference", owner, "An action names an area that does not exist.")
                );

                break;

            case OpenDoorAction open when !decor.TryGetValue(open.Decor, out ZoneDecorObject? door):
                f.Add(Error("BrokenReference", owner, $"Furni '{open.Decor}' does not exist."));

                break;

            case OpenDoorAction open when decor[open.Decor].Interaction != "door":
                f.Add(
                    Warning(
                        "NotADoor",
                        owner,
                        $"Furni '{open.Decor}' is not a door; nothing will open."
                    )
                );

                break;

            case TeleportAction tp
                when (string.IsNullOrEmpty(tp.Zone) || tp.Zone == here)
                    && !entries.Contains(tp.Entry):
                f.Add(Error("BrokenReference", owner, $"Entry '{tp.Entry}' does not exist here."));

                break;

            case TeleportAction tp
                when !string.IsNullOrEmpty(tp.Zone)
                    && tp.Zone != here
                    && !knownZones.Contains(tp.Zone):
                f.Add(Error("UnknownZone", owner, $"Teleport to unknown zone '{tp.Zone}'."));

                break;

            case StartQuestAction
            or SpawnGroupAction:
                f.Add(Inert(owner, $"action {a.GetType().Name}"));

                break;
        }
    }

    private static void CheckInteraction(
        ZoneDecorObject o,
        HashSet<string> entries,
        HashSet<string> passages,
        List<ZoneFinding> f
    )
    {
        switch (o.Interaction)
        {
            case { } kind when !Interactions.Contains(kind):
                f.Add(Error("UnknownInteraction", o.Id, $"Unknown interaction '{kind}'."));

                break;

            case "teleport"
                when o.Tag is null || !entries.Contains(o.Tag) && !passages.Contains(o.Tag):
                f.Add(
                    Error(
                        "BrokenReference",
                        o.Id,
                        $"A teleporter's tag names an entry or a passage; '{o.Tag}' is neither."
                    )
                );

                break;

            case "sign" when string.IsNullOrWhiteSpace(o.Tag):
                f.Add(Warning("EmptySign", o.Id, "This sign has no text (its tag)."));

                break;
        }
    }

    /// <summary>
    /// Plan D15: an EnterZone trigger that teleports into another area whose own trigger teleports
    /// back is a loop the server cuts at depth 8. Areas are the nodes, teleports within the zone the
    /// edges.
    /// </summary>
    private static void CheckLoops(ZoneDocument d, List<ZoneFinding> f)
    {
        Dictionary<string, ZoneEntry> entries = d
            .Entries.GroupBy(e => e.Id)
            .ToDictionary(g => g.Key, g => g.First());
        Dictionary<string, HashSet<string>> edges = [];

        foreach (
            ZoneTrigger t in d.Triggers.Where(t =>
                t.Type == ZoneTriggerType.EnterZone && t.Target is not null
            )
        )
        {
            foreach (TeleportAction tp in t.Actions.OfType<TeleportAction>())
            {
                if (
                    (string.IsNullOrEmpty(tp.Zone) || tp.Zone == d.Settings.Id)
                    && entries.TryGetValue(tp.Entry, out ZoneEntry? entry)
                )
                {
                    foreach (
                        ZoneArea into in d.Zones.Where(a =>
                            a.Id != t.Target
                            && ZoneGeometry.Covers(a.Polygon, entry.Cell.X, entry.Cell.Y)
                        )
                    )
                    {
                        (
                            edges.TryGetValue(t.Target!, out HashSet<string>? to)
                                ? to
                                : edges[t.Target!] = []
                        ).Add(into.Id);
                    }
                }
            }
        }

        foreach (string start in edges.Keys.Where(start => Reaches(edges, start, start, [])))
        {
            f.Add(
                Warning(
                    "TriggerLoop",
                    start,
                    $"Entering '{start}' teleports through areas that lead back into it."
                )
            );
        }
    }

    private static bool Reaches(
        Dictionary<string, HashSet<string>> edges,
        string from,
        string goal,
        HashSet<string> seen
    ) =>
        edges.TryGetValue(from, out HashSet<string>? next)
        && next.Any(n => n == goal || seen.Add(n) && Reaches(edges, n, goal, seen));

    /// <summary>
    /// Plan D5/D6/D24: where two areas that rule combat differently (safe, PvP, hostile) overlap at
    /// the same priority, the later one in the document wins -- legal, but rarely meant.
    /// </summary>
    private static void CheckOverlaps(ZoneDocument d, List<ZoneFinding> f)
    {
        ZoneArea[] ruling =
        [
            .. d.Zones.Where(a => CombatRule(a) is not null && a.Polygon.Count >= 3),
        ];

        for (int i = 0; i < ruling.Length; i++)
        {
            for (int j = i + 1; j < ruling.Length; j++)
            {
                ZoneArea a = ruling[i];
                ZoneArea b = ruling[j];

                if (a.Priority != b.Priority || CombatRule(a) == CombatRule(b))
                {
                    continue;
                }

                HashSet<ZoneCell> cells =
                [
                    .. ZoneGeometry.CellsInPolygon(a.Polygon, d.Floor.Width, d.Floor.Length),
                ];

                if (
                    ZoneGeometry
                        .CellsInPolygon(b.Polygon, d.Floor.Width, d.Floor.Length)
                        .Any(cells.Contains)
                )
                {
                    f.Add(
                        Warning(
                            "AmbiguousOverlap",
                            b.Id,
                            $"'{a.Id}' ({a.Kind}) and '{b.Id}' ({b.Kind}) overlap at the same priority; '{b.Id}' wins."
                        )
                    );
                }
            }
        }
    }

    /// <summary>What an area says about combat, NoCombat being Safe; null for an area that says nothing.</summary>
    private static string? CombatRule(ZoneArea area) =>
        area.Kind switch
        {
            ZoneAreaKinds.Safe or ZoneAreaKinds.NoCombat => ZoneAreaKinds.Safe,
            ZoneAreaKinds.PvP or ZoneAreaKinds.Hostile => area.Kind,
            _ => null,
        };

    /// <summary>
    /// An area's kind is one the server knows (plan D24); a Restricted one with no condition is
    /// closed to everyone until a trigger switches it off -- allowed, but worth saying.
    /// </summary>
    private static void CheckAreas(ZoneDocument d, List<ZoneFinding> f)
    {
        foreach (ZoneArea a in d.Zones)
        {
            if (!ZoneAreaKinds.All.Contains(a.Kind))
            {
                f.Add(Error("UnknownAreaKind", a.Id, $"Unknown area kind '{a.Kind}'."));
            }
            else if (a.Kind == ZoneAreaKinds.Restricted && a.Conditions.Count == 0)
            {
                f.Add(
                    Warning(
                        "ClosedArea",
                        a.Id,
                        "This restricted area has no condition: nobody enters until a trigger switches it off."
                    )
                );
            }
        }
    }

    private static ZoneFinding Inert(string id, string what) =>
        Warning("InertUntilRpg", id, $"The {what} has no effect until the RPG systems exist.");

    private static ZoneFinding Error(string code, string? id, string message) =>
        new(ZoneSeverity.Error, code, id, message);

    private static ZoneFinding Warning(string code, string? id, string message) =>
        new(ZoneSeverity.Warning, code, id, message);
}
