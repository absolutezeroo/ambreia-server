using System;
using System.Collections.Generic;
using System.Linq;
using Vortex.Rpg.Content.Definitions;

namespace Vortex.Rpg.Combat.Telegraphs;

/// <summary>
/// The cells a telegraph marks (spec M1c §8.1): a circle around its origin, a line or a cone from
/// its origin toward the aim. Walkable cells only, each once, nearest the origin first.
/// </summary>
public static class TelegraphCells
{
    public static IReadOnlyList<(int X, int Y)> Of(
        SkillTelegraph telegraph,
        (int X, int Y) origin,
        (int X, int Y) toward,
        Func<int, int, bool> walkable
    )
    {
        // ponytail: no aim (the target on the caster's own cell) points east; the runtime keeps no
        // facing to read. Plan M1c decision 4 keeps a line or a cone from aiming at the caster.
        double angle = toward == origin ? 0 : Math.Atan2(toward.Y - origin.Y, toward.X - origin.X);
        int size = telegraph.Size;

        IEnumerable<(int X, int Y)> cells = telegraph.Shape switch
        {
            TelegraphShape.Circle => Square(origin, size).Where(c => Sq(c, origin) <= size * size),
            TelegraphShape.Line => Line(origin, angle, size, telegraph.Width),
            _ => Square(origin, size).Where(c => InCone(c, origin, angle, size, telegraph.Angle)),
        };

        return
        [
            .. cells
                .Where(c => walkable(c.X, c.Y))
                .Distinct()
                .OrderBy(c => Sq(c, origin))
                .ThenBy(c => c.Y)
                .ThenBy(c => c.X),
        ];
    }

    /// <summary>
    /// Straight ahead in the nearest of the 8 directions, <paramref name="size"/> steps long and
    /// widened on both sides. Cells are kept by their projection on the direction (ahead, within the
    /// length) and across it (within the half width), so a wide diagonal has no holes.
    /// </summary>
    private static IEnumerable<(int X, int Y)> Line(
        (int X, int Y) origin,
        double angle,
        int size,
        int width
    )
    {
        double octant = Math.Round(angle / (Math.PI / 4)) * (Math.PI / 4);
        int dx = Math.Sign(Math.Round(Math.Cos(octant), 6));
        int dy = Math.Sign(Math.Round(Math.Sin(octant), 6));
        int half = (width - 1) / 2;
        // A diagonal step is (1, 1): its projection counts 2 per step.
        int length = size * (dx * dx + dy * dy);

        return Square(origin, size + half)
            .Where(c =>
            {
                int along = (c.X - origin.X) * dx + (c.Y - origin.Y) * dy;
                int across = (c.Y - origin.Y) * dx - (c.X - origin.X) * dy;

                return along >= 1 && along <= length && Math.Abs(across) <= half;
            });
    }

    private static bool InCone(
        (int X, int Y) c,
        (int X, int Y) origin,
        double angle,
        int size,
        int opening
    )
    {
        double distance = Math.Sqrt(Sq(c, origin));

        if (distance < 1 || distance > size)
        {
            return false;
        }

        double off = Math.Abs(
            Math.IEEERemainder(Math.Atan2(c.Y - origin.Y, c.X - origin.X) - angle, 2 * Math.PI)
        );

        // The epsilon keeps the cells exactly on the edges (45° of a 90° cone).
        return off <= opening * Math.PI / 360 + 1e-9;
    }

    private static IEnumerable<(int X, int Y)> Square((int X, int Y) origin, int size)
    {
        for (int y = origin.Y - size; y <= origin.Y + size; y++)
        {
            for (int x = origin.X - size; x <= origin.X + size; x++)
            {
                yield return (x, y);
            }
        }
    }

    private static int Sq((int X, int Y) a, (int X, int Y) b) =>
        (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);
}
