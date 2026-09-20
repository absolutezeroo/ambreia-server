using System;

namespace Vortex.Primitives.Rooms.Enums;

/// <summary>
/// Working out where somebody is standing in relation to a piece of furniture.
/// </summary>
/// <remarks>
/// Pure and tiny on purpose: it is called for every piece of furniture beside every tile an avatar
/// walks onto, so it allocates nothing and does one subtraction and one modulo.
/// </remarks>
public static class RelativePositions
{
    /// <summary>The value a signal carries for a position, and content stores.</summary>
    public static string ToValue(this RelativePosition position) =>
        position switch
        {
            RelativePosition.On => "on",
            RelativePosition.Front => "front",
            RelativePosition.FrontRight => "front_right",
            RelativePosition.Right => "right",
            RelativePosition.BackRight => "back_right",
            RelativePosition.Back => "back",
            RelativePosition.BackLeft => "back_left",
            RelativePosition.Left => "left",
            _ => "front_left",
        };

    /// <summary>
    /// Where <paramref name="fromX"/>/<paramref name="fromY"/> stands in relation to the furniture
    /// at <paramref name="itemX"/>/<paramref name="itemY"/>, or null when it is not beside it.
    /// </summary>
    /// <param name="itemRotation">
    /// The way the furniture faces. Everything here is measured against it, which is what makes
    /// "in front of" survive the furniture being turned round.
    /// </param>
    /// <remarks>
    /// Null beyond one tile rather than a coarse octant. "In front of the television" said of
    /// somebody five tiles away is not a claim anybody means, and answering it would make a filter
    /// match most of the room.
    /// </remarks>
    public static RelativePosition? Of(
        int fromX,
        int fromY,
        int itemX,
        int itemY,
        Rotation itemRotation
    )
    {
        int dx = fromX - itemX;
        int dy = fromY - itemY;

        if (Math.Abs(dx) > 1 || Math.Abs(dy) > 1)
        {
            return null;
        }

        if (dx == 0 && dy == 0)
        {
            return RelativePosition.On;
        }

        // A furniture with no rotation of its own has no front, so everything beside it is measured
        // from north and the content reads as compass directions. Better than refusing: a rug still
        // has a "north side", and a filter on one is at least honest about what it means.
        int facing = itemRotation == Rotation.None ? 0 : (int)itemRotation;
        int absolute = (int)RotationExtensions.FromDelta(dx, dy);

        return (RelativePosition)(1 + (((absolute - facing) % 8) + 8) % 8);
    }
}
