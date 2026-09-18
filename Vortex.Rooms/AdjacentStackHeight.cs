using System.Collections.Generic;

namespace Vortex.Rooms;

/// <summary>
/// The rule behind the magic tile widget's up and down arrows: of every surface on the tile, which
/// one is the next in the direction asked for.
/// </summary>
/// <remarks>
/// Its own type because it is the only branching in
/// <c>RoomGrain.SetAdjacentCustomStackHeightAsync</c>, and standing a room up with a stack of
/// furniture in it to exercise four comparisons is not a trade worth making.
/// </remarks>
public static class AdjacentStackHeight
{
    /// <summary>
    /// The nearest candidate strictly past <paramref name="current"/> in the chosen direction, or
    /// null when there is none — which is the arrow doing nothing, not an error.
    /// </summary>
    /// <param name="candidates">
    /// Every surface on the tile: each other item's underside and its top, plus the floor. They may
    /// repeat and arrive in any order.
    /// </param>
    /// <param name="down">The down arrow; false is up.</param>
    /// <param name="max">The room's stack ceiling. Anything past it is not offered.</param>
    public static double? Pick(
        double current,
        IEnumerable<double> candidates,
        bool down,
        double max
    )
    {
        double? target = null;

        foreach (double candidate in candidates)
        {
            if (candidate < 0 || candidate > max)
            {
                continue;
            }

            // Strictly past, so a press always moves: a surface level with the tile is where it
            // already is.
            if (down ? candidate >= current : candidate <= current)
            {
                continue;
            }

            // Nearest wins, so repeated presses walk the stack one surface at a time instead of
            // jumping to its far end.
            bool better =
                target is null || (down ? candidate > target.Value : candidate < target.Value);

            if (better)
            {
                target = candidate;
            }
        }

        return target;
    }
}
