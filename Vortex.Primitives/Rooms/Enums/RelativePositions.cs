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
    /// <param name="width">The furniture's width, before rotation. One for most pieces.</param>
    /// <param name="length">And its length. A sofa is 2x1, a tile puzzle can be 3x3, a pool 2x6.</param>
    /// <remarks>
    /// <para>
    /// Null beyond one tile rather than a coarse octant. "In front of the television" said of
    /// somebody five tiles away is not a claim anybody means, and answering it would make a filter
    /// match most of the room.
    /// </para>
    /// <para>
    /// Measured against the whole footprint, not the anchor tile. A piece occupies every tile it
    /// covers -- the room registers it on all of them -- so standing at the far end of a 2x6 is
    /// standing beside that piece. Measured from the anchor, the delta was five tiles and this
    /// answered null: no signal at all, for most of the length of every long piece in the hotel.
    /// Nothing threw and nothing logged; the task simply never advanced.
    /// </para>
    /// </remarks>
    public static RelativePosition? Of(
        int fromX,
        int fromY,
        int itemX,
        int itemY,
        Rotation itemRotation,
        int width = 1,
        int length = 1
    )
    {
        // A turned piece covers a turned rectangle. The room's own footprint rule, because the two
        // disagreeing is how an item comes to be beside a tile it does not occupy.
        if (width > 0 && length > 0 && itemRotation is Rotation.East or Rotation.West)
        {
            (width, length) = (length, width);
        }

        // The nearest tile OF the piece, which for a 1x1 is the piece itself -- so everything below
        // is what it always was for the pieces that were already right.
        int nearX = Math.Clamp(fromX, itemX, itemX + Math.Max(width, 1) - 1);
        int nearY = Math.Clamp(fromY, itemY, itemY + Math.Max(length, 1) - 1);

        int dx = fromX - nearX;
        int dy = fromY - nearY;

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
