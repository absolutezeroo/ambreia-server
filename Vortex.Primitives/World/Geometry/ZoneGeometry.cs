using System;
using System.Collections.Generic;
using Vortex.Primitives.World.Layers;

namespace Vortex.Primitives.World.Geometry;

/// <summary>
/// The zone document's cell geometry, the same rule as the editor's (map-editor-core
/// <c>cellsInPolygon</c>): a polygon of cell positions covers the cells inside it and on its
/// edges, so an area drawn from (1,1) to (3,3) covers nine cells, not four.
/// </summary>
public static class ZoneGeometry
{
    public static List<ZoneCell> CellsInPolygon(
        IReadOnlyList<ZoneCell> points,
        int width,
        int length
    )
    {
        List<ZoneCell> cells = [];

        if (points.Count == 0)
        {
            return cells;
        }

        for (int y = 0; y < length; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (Covers(points, x, y))
                {
                    cells.Add(new ZoneCell(x, y));
                }
            }
        }

        return cells;
    }

    /// <summary>Is (x, y) inside the polygon or on one of its edges?</summary>
    public static bool Covers(IReadOnlyList<ZoneCell> points, int x, int y)
    {
        bool inside = false;

        for (int i = 0, j = points.Count - 1; i < points.Count; j = i++)
        {
            (int xi, int yi) = (points[i].X, points[i].Y);
            (int xj, int yj) = (points[j].X, points[j].Y);

            if (OnSegment(x, y, xi, yi, xj, yj))
            {
                return true;
            }

            if ((yi > y) != (yj > y) && x < (double)(xj - xi) * (y - yi) / (yj - yi) + xi)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private static bool OnSegment(int px, int py, int ax, int ay, int bx, int by) =>
        (bx - ax) * (py - ay) - (by - ay) * (px - ax) == 0
        && px >= Math.Min(ax, bx)
        && px <= Math.Max(ax, bx)
        && py >= Math.Min(ay, by)
        && py <= Math.Max(ay, by);
}
