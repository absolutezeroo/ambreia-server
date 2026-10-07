using System.Collections.Generic;
using System.Linq;
using Vortex.Primitives.World;
using Vortex.Primitives.World.Geometry;
using Vortex.Primitives.World.Layers;

namespace Vortex.Rooms.Grains.Zones;

/// <summary>Builds the <see cref="CompiledZone"/> of a published document. Pure.</summary>
public static class ZoneCompiler
{
    public static CompiledZone Compile(ZoneDocument doc)
    {
        int width = doc.Floor.Width;
        int length = doc.Floor.Length;
        int cells = width * length;
        List<ZoneArea>[] areasAt = new List<ZoneArea>[cells];
        ZonePassage?[] passageAt = new ZonePassage?[cells];
        Dictionary<string, IReadOnlySet<int>> areaTiles = [];

        // Highest priority first; at equal priority the later area of the document wins, so it
        // is placed first: iterate in reverse and keep the sort stable.
        foreach (ZoneArea area in Enumerable.Reverse(doc.Zones))
        {
            HashSet<int> tiles =
            [
                .. ZoneGeometry
                    .CellsInPolygon(area.Polygon, width, length)
                    .Select(c => c.Y * width + c.X),
            ];

            areaTiles.TryAdd(area.Id, tiles);

            foreach (int tile in tiles)
            {
                (areasAt[tile] ??= []).Add(area);
            }
        }

        foreach (List<ZoneArea>? list in areasAt)
        {
            if (list is { Count: > 1 })
            {
                List<ZoneArea> sorted = [.. list.OrderByDescending(a => a.Priority)];

                list.Clear();
                list.AddRange(sorted);
            }
        }

        // The first passage of the document wins a shared cell.
        foreach (ZonePassage passage in Enumerable.Reverse(doc.Passages))
        {
            foreach (ZoneCell cell in passage.Area)
            {
                if (cell.X >= 0 && cell.Y >= 0 && cell.X < width && cell.Y < length)
                {
                    passageAt[cell.Y * width + cell.X] = passage;
                }
            }
        }

        // The ids ZoneRoomLoader gives the decor: floor objects i + 1, wall objects after them.
        Dictionary<string, int> objectIds = [];

        for (int i = 0; i < doc.Decor.Count; i++)
        {
            objectIds.TryAdd(doc.Decor[i].Id, i + 1);
        }

        for (int j = 0; j < doc.WallDecor.Count; j++)
        {
            objectIds.TryAdd(doc.WallDecor[j].Id, doc.Decor.Count + j + 1);
        }

        IReadOnlyList<ZoneArea>[] frozen =
        [
            .. areasAt.Select(l => (IReadOnlyList<ZoneArea>?)l ?? []),
        ];

        return new CompiledZone(doc, frozen, passageAt, objectIds, areaTiles);
    }
}
