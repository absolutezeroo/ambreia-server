using System.Collections.Generic;
using System.Linq;
using Vortex.Primitives.World;
using Vortex.Primitives.World.Geometry;
using Vortex.Primitives.World.Layers;
using Vortex.Primitives.World.Rules;

namespace Vortex.World;

/// <summary>
/// The checks that follow what the server does with a zone at run time (plan phase 6, spec Z1 §9):
/// a trigger aims at what its type listens to, an action and a teleporter name what exists, a rule
/// or a trigger type no module of this server runs is flagged inert, triggers teleporting into each
/// other and Safe/PvP overlaps warn, and a spawn that sleeps until a trigger wakes it is said so.
/// </summary>
internal static class ZoneRuntimeRules
{
    /// <summary>The trigger types the room fires itself; any other is a module's to declare.</summary>
    private static readonly HashSet<ZoneTriggerType> RoomTriggers =
    [
        ZoneTriggerType.EnterZone,
        ZoneTriggerType.LeaveZone,
        ZoneTriggerType.Interact,
        ZoneTriggerType.Timer,
    ];

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

    public static void Check(
        ZoneDocument d,
        IReadOnlySet<string> knownZones,
        ZoneRuleVocabulary v,
        List<ZoneFinding> f
    )
    {
        HashSet<string> areas = [.. d.Zones.Select(z => z.Id)];
        Dictionary<string, ZoneDecorObject> decor = d
            .Decor.GroupBy(o => o.Id)
            .ToDictionary(g => g.Key, g => g.First());
        HashSet<string> entries = [.. d.Entries.Select(e => e.Id)];
        HashSet<string> passages = [.. d.Passages.Select(p => p.Id)];
        HashSet<string> known =
        [
            .. areas,
            .. decor.Keys,
            .. d.WallDecor.Select(w => w.Id),
            .. d.Npcs.Select(n => n.Id),
            .. d.Spawns.Select(s => s.Id),
        ];
        string here = d.Settings.Id;

        // What a MonsterKilled aims at, and what a SpawnGroup names: a spawn, its monster, its group.
        HashSet<string> spawnTargets =
        [
            .. d.Spawns.Select(s => s.Id),
            .. d.Spawns.Select(s => s.Monster),
        ];
        HashSet<string> spawnNames =
        [
            .. d.Spawns.Select(s => s.Id),
            .. d.Spawns.Select(s => s.Group).OfType<string>(),
        ];

        foreach (ZoneTrigger t in d.Triggers)
        {
            CheckTarget(t, areas, decor, known, spawnTargets, v, f);

            foreach (ZoneCondition c in t.Conditions)
            {
                CheckCondition(t.Id, c, areas, v, f);
            }

            foreach (ZoneAction a in t.Actions)
            {
                CheckAction(t.Id, a, areas, decor, entries, here, knownZones, spawnNames, v, f);
            }
        }

        CheckDormantSpawns(d, f);

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
                CheckCondition(p.Id, c, areas, v, f);
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

    /// <param name="known">Every element id a trigger may name (areas, decor, NPCs, spawns): one that
    /// names none of them is the BrokenReference of ZoneValidator, not a wrong target.</param>
    private static void CheckTarget(
        ZoneTrigger t,
        HashSet<string> areas,
        Dictionary<string, ZoneDecorObject> decor,
        HashSet<string> known,
        HashSet<string> spawnTargets,
        ZoneRuleVocabulary v,
        List<ZoneFinding> f
    )
    {
        if (!RoomTriggers.Contains(t.Type) && !v.Raised.Contains(t.Type))
        {
            f.Add(Inert(t.Id, $"trigger {t.Type}"));

            return;
        }

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

            case ZoneTriggerType.EnterZone
            or ZoneTriggerType.LeaveZone
                when t.Target is null || known.Contains(t.Target) && !areas.Contains(t.Target):
                f.Add(
                    Error(
                        "WrongTarget",
                        t.Id,
                        $"{t.Type} listens to an area; '{t.Target}' is not one."
                    )
                );

                break;

            // Only a floor furni is used (UseFurniture): on a wall item, an NPC or an area the
            // trigger would never fire.
            case ZoneTriggerType.Interact
                when t.Target is null || known.Contains(t.Target) && !decor.ContainsKey(t.Target):
                f.Add(
                    Error(
                        "WrongTarget",
                        t.Id,
                        $"Interact listens to a floor furni; '{t.Target}' is not one."
                    )
                );

                break;

            case ZoneTriggerType.MonsterKilled
                when t.Target is null || !spawnTargets.Contains(t.Target):
                f.Add(
                    Error(
                        "WrongTarget",
                        t.Id,
                        $"MonsterKilled listens to a spawn of this zone or its monster; '{t.Target}' is neither."
                    )
                );

                break;

            case ZoneTriggerType.PlayerLevel
                when !int.TryParse(t.Target, out int level) || level < 2:
                f.Add(
                    Error(
                        "WrongTarget",
                        t.Id,
                        $"PlayerLevel listens to a level reached, 2 or more, not '{t.Target}'."
                    )
                );

                break;

            case ZoneTriggerType.ItemOwned:
                CheckItem(t.Id, t.Target ?? string.Empty, v, f);

                break;
        }
    }

    private static void CheckCondition(
        string owner,
        ZoneCondition c,
        HashSet<string> areas,
        ZoneRuleVocabulary v,
        List<ZoneFinding> f
    )
    {
        if (!v.Declared.Contains(c.GetType()))
        {
            f.Add(Inert(owner, $"condition {c.GetType().Name}, always false"));

            return;
        }

        switch (c)
        {
            case ZoneActiveCondition active when !areas.Contains(active.Zone):
                f.Add(Error("BrokenReference", owner, $"Area '{active.Zone}' does not exist."));

                break;

            case PlayerLevelCondition level
                when level.Min < 1 || level.Max != 0 && level.Max < level.Min:
                f.Add(
                    Error(
                        "BadLevel",
                        owner,
                        $"A level from {level.Min} to {level.Max}: the least is 1, the most 0 (no cap) or not below it."
                    )
                );

                break;

            case HasItemCondition item:
                CheckItem(owner, item.Item, v, f);

                break;
        }
    }

    /// <summary>An item a rule names is in the content; without content loaded it is not checked.</summary>
    private static void CheckItem(
        string owner,
        string item,
        ZoneRuleVocabulary v,
        List<ZoneFinding> f
    )
    {
        if (v.Items is null)
        {
            f.Add(
                Warning(
                    "UnresolvedReference",
                    owner,
                    $"item '{item}' is not checked until its system exists."
                )
            );
        }
        else if (!v.Items.Contains(item))
        {
            f.Add(Error("UnknownItem", owner, $"No item '{item}' in the content."));
        }
    }

    /// <summary>Spec Z1 §6.1: the author sees which spawns appear only when a trigger wakes them.</summary>
    private static void CheckDormantSpawns(ZoneDocument d, List<ZoneFinding> f)
    {
        HashSet<string> woken =
        [
            .. d
                .Triggers.SelectMany(t => t.Actions)
                .OfType<SpawnGroupAction>()
                .Select(a => a.Spawn),
        ];

        foreach (
            ZoneSpawn s in d.Spawns.Where(s =>
                woken.Contains(s.Id) || s.Group is { } g && woken.Contains(g)
            )
        )
        {
            f.Add(
                Warning(
                    "DormantSpawn",
                    s.Id,
                    "This spawn sleeps until a SpawnGroup action wakes it: one wave, never respawned."
                )
            );
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
        HashSet<string> spawnNames,
        ZoneRuleVocabulary v,
        List<ZoneFinding> f
    )
    {
        if (!v.Declared.Contains(a.GetType()))
        {
            f.Add(Inert(owner, $"action {a.GetType().Name}"));

            return;
        }

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

            case SpawnGroupAction group when !spawnNames.Contains(group.Spawn):
                f.Add(
                    Error(
                        "BrokenReference",
                        owner,
                        $"No spawn or spawn group '{group.Spawn}' in this zone."
                    )
                );

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
        Warning(
            "InertUntilRpg",
            id,
            $"The {what} has no effect: no system of this server carries it out yet."
        );

    private static ZoneFinding Error(string code, string? id, string message) =>
        new(ZoneSeverity.Error, code, id, message);

    private static ZoneFinding Warning(string code, string? id, string message) =>
        new(ZoneSeverity.Warning, code, id, message);
}
