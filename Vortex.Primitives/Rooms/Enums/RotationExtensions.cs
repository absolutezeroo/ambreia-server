using System;

namespace Vortex.Primitives.Rooms.Enums;

public static class RotationExtensions
{
    public static readonly Rotation[] CARDINAL =
    {
        Rotation.North,
        Rotation.East,
        Rotation.South,
        Rotation.West,
    };

    public static Rotation FromDelta(int dx, int dy)
    {
        dx = Math.Clamp(dx, -1, 1);
        dy = Math.Clamp(dy, -1, 1);

        if (dx == 0 && dy == 0)
        {
            throw new ArgumentException("Zero delta has no direction.");
        }

        if (dx == 0 && dy < 0)
        {
            return Rotation.North;
        }

        if (dx > 0 && dy < 0)
        {
            return Rotation.NorthEast;
        }

        if (dx > 0 && dy == 0)
        {
            return Rotation.East;
        }

        if (dx > 0 && dy > 0)
        {
            return Rotation.SouthEast;
        }

        if (dx == 0 && dy > 0)
        {
            return Rotation.South;
        }

        if (dx < 0 && dy > 0)
        {
            return Rotation.SouthWest;
        }

        if (dx < 0 && dy == 0)
        {
            return Rotation.West;
        }

        return Rotation.NorthWest;
    }

    public static Rotation FromPoints(int fromX, int fromY, int toX, int toY) =>
        FromDelta(toX - fromX, toY - fromY);

    /// <summary>
    /// The one-tile step this rotation points at — the inverse of <see cref="FromDelta"/>, for the
    /// furniture that has to reason about what is in front of it or behind it.
    /// </summary>
    public static (int Dx, int Dy) ToDelta(this Rotation rot) =>
        rot switch
        {
            Rotation.North => (0, -1),
            Rotation.NorthEast => (1, -1),
            Rotation.East => (1, 0),
            Rotation.SouthEast => (1, 1),
            Rotation.South => (0, 1),
            Rotation.SouthWest => (-1, 1),
            Rotation.West => (-1, 0),
            _ => (-1, -1),
        };

    public static Rotation ToSitRotation(this Rotation rot) => (int)rot % 2 > 0 ? rot - 1 : rot;

    public static Rotation Rotate(this Rotation rot, int delta) =>
        (Rotation)(((int)rot + delta + 8) % 8);
}
