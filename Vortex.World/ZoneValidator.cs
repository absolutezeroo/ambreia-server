using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Options;
using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Furniture.Snapshots;
using Vortex.Primitives.World;
using Vortex.Primitives.World.Layers;

namespace Vortex.World;

/// <summary>The rules of spec §5. Errors refuse a save; warnings do not.</summary>
public sealed class ZoneValidator(
    IFurnitureDefinitionProvider definitions,
    IOptions<WorldConfig> config
)
{
    private static readonly int[] AllowedRotations = [0, 2, 4, 6];

    public IReadOnlyList<ZoneFinding> Validate(ZoneDocument doc, IReadOnlySet<string> knownZones)
    {
        List<ZoneFinding> f = [];
        ZoneFloor floor = doc.Floor;

        if (doc.SchemaVersion != ZoneDocument.CurrentSchemaVersion)
        {
            f.Add(
                Error(
                    "SchemaVersion",
                    null,
                    $"Schema {doc.SchemaVersion}, server reads {ZoneDocument.CurrentSchemaVersion}."
                )
            );
        }

        if (
            floor.Width is < 1 or > ZoneDocument.MaxSize
            || floor.Length is < 1 or > ZoneDocument.MaxSize
        )
        {
            f.Add(
                Error(
                    "FloorTooLarge",
                    null,
                    $"Floor {floor.Width}×{floor.Length}; at most {ZoneDocument.MaxSize}×{ZoneDocument.MaxSize}."
                )
            );
            return f;
        }

        if (floor.Tiles.Count != floor.Width * floor.Length)
        {
            f.Add(
                Error(
                    "FloorTileCount",
                    null,
                    $"{floor.Tiles.Count} tiles for {floor.Width}×{floor.Length}."
                )
            );
            return f;
        }

        for (int i = 0; i < floor.Tiles.Count; i++)
        {
            if (floor.Tiles[i].Height is < 0 or > ZoneDocument.MaxTileHeight)
            {
                f.Add(
                    Error(
                        "TileHeight",
                        null,
                        $"Tile ({i % floor.Width},{i / floor.Width}) height {floor.Tiles[i].Height}."
                    )
                );
            }
        }

        if (!Walkable(floor, floor.Door))
        {
            f.Add(Error("DoorNotWalkable", null, $"Door {floor.Door} is not a walkable tile."));
        }

        CheckIds(doc, f);
        CheckDecor(doc, f);
        CheckInternalReferences(doc, f);

        foreach (ZonePassage p in doc.Passages.Where(p => !knownZones.Contains(p.DestinationZone)))
        {
            f.Add(Error("UnknownZone", p.Id, $"Passage to unknown zone '{p.DestinationZone}'."));
        }

        CheckReachability(doc, f);
        DeferredReferences(doc, f);

        if (doc.Decor.Count + doc.WallDecor.Count > config.Value.DecorBudget)
        {
            f.Add(
                Warning(
                    "OverBudget",
                    null,
                    $"{doc.Decor.Count + doc.WallDecor.Count} decor objects, budget {config.Value.DecorBudget}."
                )
            );
        }

        return f;
    }

    private static void CheckIds(ZoneDocument d, List<ZoneFinding> f)
    {
        IEnumerable<string> ids = d
            .Decor.Select(x => x.Id)
            .Concat(d.WallDecor.Select(x => x.Id))
            .Concat(d.Entries.Select(x => x.Id))
            .Concat(d.Passages.Select(x => x.Id))
            .Concat(d.Zones.Select(x => x.Id))
            .Concat(d.Npcs.Select(x => x.Id))
            .Concat(d.Spawns.Select(x => x.Id))
            .Concat(d.Resources.Select(x => x.Id))
            .Concat(d.Chests.Select(x => x.Id))
            .Concat(d.Interactives.Select(x => x.Id))
            .Concat(d.Bosses.Select(x => x.Id))
            .Concat(d.Events.Select(x => x.Id))
            .Concat(d.Triggers.Select(x => x.Id));

        foreach (string id in ids.GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key))
        {
            f.Add(Error("DuplicateId", id, $"Id '{id}' is used more than once."));
        }
    }

    private void CheckDecor(ZoneDocument d, List<ZoneFinding> f)
    {
        foreach (ZoneDecorObject o in d.Decor)
        {
            FurnitureDefinitionSnapshot? def = definitions.TryGetDefinition(o.Definition);

            if (def is null || def.ProductType != ProductType.Floor)
            {
                f.Add(
                    Error(
                        "UnknownFurniture",
                        o.Id,
                        $"No floor furniture {o.Definition} ({o.Class})."
                    )
                );
                continue;
            }

            if (!string.Equals(def.Name, o.Class, StringComparison.Ordinal))
            {
                f.Add(
                    Warning(
                        "ClassMismatch",
                        o.Id,
                        $"Definition {o.Definition} is '{def.Name}', not '{o.Class}'."
                    )
                );
            }

            if (!AllowedRotations.Contains(o.Rotation))
            {
                f.Add(Error("BadRotation", o.Id, $"Rotation {o.Rotation}; allowed 0, 2, 4, 6."));
            }

            if (o.State < 0 || (def.TotalStates > 0 && o.State >= def.TotalStates))
            {
                f.Add(
                    Error(
                        "BadState",
                        o.Id,
                        $"State {o.State}; the furniture has {def.TotalStates}."
                    )
                );
            }

            (int w, int l) = o.Rotation is 2 or 6
                ? (def.Length, def.Width)
                : (def.Width, def.Length);

            if (o.X < 0 || o.Y < 0 || o.X + w > d.Floor.Width || o.Y + l > d.Floor.Length)
            {
                f.Add(
                    Error(
                        "OutOfFloor",
                        o.Id,
                        $"Footprint {w}×{l} at ({o.X},{o.Y}) leaves the floor."
                    )
                );
            }
        }

        foreach (ZoneWallDecorObject o in d.WallDecor)
        {
            FurnitureDefinitionSnapshot? def = definitions.TryGetDefinition(o.Definition);

            if (def is null || def.ProductType != ProductType.Wall)
            {
                f.Add(
                    Error(
                        "UnknownFurniture",
                        o.Id,
                        $"No wall furniture {o.Definition} ({o.Class})."
                    )
                );
            }
        }
    }

    private static void CheckInternalReferences(ZoneDocument d, List<ZoneFinding> f)
    {
        HashSet<string> zones = [.. d.Zones.Select(z => z.Id)];
        HashSet<string> decor =
        [
            .. d.Decor.Select(x => x.Id).Concat(d.WallDecor.Select(x => x.Id)),
        ];
        HashSet<string> all =
        [
            .. zones,
            .. decor,
            .. d.Npcs.Select(x => x.Id),
            .. d.Spawns.Select(x => x.Id),
        ];

        foreach (
            ZoneTrigger t in d.Triggers.Where(t => t.Target is not null && !all.Contains(t.Target))
        )
        {
            f.Add(Error("BrokenReference", t.Id, $"Target '{t.Target}' does not exist."));
        }

        foreach (
            ZoneTile tile in d.Floor.Tiles.Where(t => t.Zone is not null && !zones.Contains(t.Zone))
        )
        {
            f.Add(
                Error(
                    "BrokenReference",
                    tile.Zone,
                    $"A tile names zone '{tile.Zone}', which does not exist."
                )
            );
        }

        foreach (ZoneChest c in d.Chests.Where(c => !decor.Contains(c.Decor)))
        {
            f.Add(Error("BrokenReference", c.Id, $"Chest decor '{c.Decor}' does not exist."));
        }

        foreach (ZoneInteractive i in d.Interactives.Where(i => !decor.Contains(i.Decor)))
        {
            f.Add(Error("BrokenReference", i.Id, $"Interactive decor '{i.Decor}' does not exist."));
        }
    }

    /// <summary>Walkable tiles connected to the door by 8-neighbour steps of at most one height.</summary>
    private static void CheckReachability(ZoneDocument d, List<ZoneFinding> f)
    {
        ZoneFloor floor = d.Floor;
        bool[] seen = new bool[floor.Tiles.Count];
        Queue<ZoneCell> open = new();

        if (Walkable(floor, floor.Door))
        {
            open.Enqueue(floor.Door);
            seen[Index(floor, floor.Door)] = true;
        }

        while (open.TryDequeue(out ZoneCell c))
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    ZoneCell n = new(c.X + dx, c.Y + dy);

                    if ((dx, dy) == (0, 0) || !Walkable(floor, n) || seen[Index(floor, n)])
                    {
                        continue;
                    }

                    if (
                        Math.Abs(
                            floor.Tiles[Index(floor, n)].Height
                                - floor.Tiles[Index(floor, c)].Height
                        ) > 1
                    )
                    {
                        continue;
                    }

                    seen[Index(floor, n)] = true;
                    open.Enqueue(n);
                }
            }
        }

        bool Reached(ZoneCell cell) => Inside(floor, cell) && seen[Index(floor, cell)];

        foreach (ZoneSpawn s in d.Spawns.Where(s => !s.Area.Any(Reached)))
        {
            f.Add(
                Warning(
                    "Unreachable",
                    s.Id,
                    "No tile of this spawn can be walked to from the door."
                )
            );
        }

        foreach (ZoneNpc n in d.Npcs.Where(n => !Reached(n.Cell)))
        {
            f.Add(Warning("Unreachable", n.Id, "This NPC cannot be walked to from the door."));
        }

        foreach (ZonePassage p in d.Passages.Where(p => !p.Area.Any(Reached)))
        {
            f.Add(Warning("Unreachable", p.Id, "This passage cannot be walked to from the door."));
        }

        foreach (ZoneResource r in d.Resources.Where(r => r.Cell is { } c && !Reached(c)))
        {
            f.Add(Warning("Unreachable", r.Id, "This resource cannot be walked to from the door."));
        }
    }

    /// <summary>Content the RPG systems will own; nothing resolves it yet (spec §5.3).</summary>
    private static void DeferredReferences(ZoneDocument d, List<ZoneFinding> f)
    {
        foreach (ZoneNpc n in d.Npcs)
        {
            Unresolved(f, n.Id, "npc template", n.Template);
        }

        foreach (ZoneSpawn s in d.Spawns)
        {
            Unresolved(f, s.Id, "monster", s.Monster);
        }

        foreach (ZoneResource r in d.Resources)
        {
            Unresolved(f, r.Id, "resource", r.Template);
        }

        foreach (ZoneChest c in d.Chests)
        {
            Unresolved(f, c.Id, "loot table", c.LootTable);
        }

        foreach (ZoneBoss b in d.Bosses)
        {
            Unresolved(f, b.Id, "encounter", b.Encounter);
        }

        foreach (ZoneEvent e in d.Events)
        {
            Unresolved(f, e.Id, "event", e.Definition);
        }
    }

    private static void Unresolved(List<ZoneFinding> f, string id, string what, string reference) =>
        f.Add(
            Warning(
                "UnresolvedReference",
                id,
                $"{what} '{reference}' is not checked until its system exists."
            )
        );

    private static bool Inside(ZoneFloor f, ZoneCell c) =>
        c.X >= 0 && c.Y >= 0 && c.X < f.Width && c.Y < f.Length;

    private static int Index(ZoneFloor f, ZoneCell c) => c.Y * f.Width + c.X;

    private static bool Walkable(ZoneFloor f, ZoneCell c) =>
        Inside(f, c) && f.Tiles[Index(f, c)] is { Walkable: true, Hole: false };

    private static ZoneFinding Error(string code, string? id, string message) =>
        new(ZoneSeverity.Error, code, id, message);

    private static ZoneFinding Warning(string code, string? id, string message) =>
        new(ZoneSeverity.Warning, code, id, message);
}
