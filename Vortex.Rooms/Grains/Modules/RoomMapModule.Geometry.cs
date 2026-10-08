using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Vortex.Logging;
using Vortex.Primitives;
using Vortex.Primitives.Rooms.Enums;

namespace Vortex.Rooms.Grains.Modules;

/// <summary>The map's coordinates: indexes, bounds, directions and footprints. Pure arithmetic on
/// the model's width and height; nothing here reads a tile.</summary>
public sealed partial class RoomMapModule
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ToIdx(int x, int y)
    {
        return y * Width + x;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetX(int idx)
    {
        return idx % Width;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetY(int idx)
    {
        return idx / Width;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetDistanceBetween(int a, int b)
    {
        return Math.Abs((a % Width) - (b % Width)) + Math.Abs(a / Width - b / Width);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool InBounds(int x, int y)
    {
        return (uint)x < (uint)Width && (uint)y < (uint)Height;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool InBounds(int idx)
    {
        return (uint)idx < (uint)(Width * Height);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsDiagonal(int idxA, int idxB)
    {
        int delta = idxB - idxA;

        if (delta != Width + 1 && delta != Width - 1 && delta != -Width + 1 && delta != -Width - 1)
        {
            return false;
        }

        int ax = idxA % Width;
        int bx = idxB % Width;

        return Math.Abs(ax - bx) == 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public (int dx, int dy) GetDirectionOffset(Rotation dir)
    {
        return dir switch
        {
            Rotation.North => (0, -1),
            Rotation.NorthEast => (1, -1),
            Rotation.East => (1, 0),
            Rotation.SouthEast => (1, 1),
            Rotation.South => (0, 1),
            Rotation.SouthWest => (-1, 1),
            Rotation.West => (-1, 0),
            Rotation.NorthWest => (-1, -1),
            _ => (0, 0),
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetTileInFront(int index, Rotation direction, out int nextIndex)
    {
        int x = GetX(index);
        int y = GetY(index);

        (int dx, int dy) = GetDirectionOffset(direction);

        int nx = x + dx;
        int ny = y + dy;

        if (!InBounds(nx, ny))
        {
            nextIndex = -1;

            return false;
        }

        nextIndex = ToIdx(nx, ny);

        return true;
    }

    public (int x, int y) GetTileXY(int idx)
    {
        if (!InBounds(idx))
        {
            throw new VortexException(VortexErrorCodeEnum.TileOutOfBounds);
        }

        return (GetX(idx), GetY(idx));
    }

    public bool GetTileIdForSize(
        int x,
        int y,
        Rotation rot,
        int width,
        int length,
        out List<int> tileIds
    )
    {
        tileIds = [];

        if (width > 0 && length > 0)
        {
            if (rot == Rotation.East || rot == Rotation.West)
            {
                (width, length) = (length, width);
            }
        }

        for (int minX = x; minX < x + width; minX++)
        {
            for (int minY = y; minY < y + length; minY++)
            {
                // Bounds must be checked on the coordinates, not on the flattened index: ToIdx
                // folds x into the row stride, so an x past Width does not leave the array, it
                // silently lands on a real tile in a later row (x=Width,y=3 in a 10-wide room
                // yields idx 40, which is tile (0,4)). InBounds(idx) accepts that, and a footprint
                // hanging over the room edge would validate against tiles on the opposite side.
                if (!InBounds(minX, minY))
                {
                    throw new VortexException(VortexErrorCodeEnum.TileOutOfBounds);
                }

                tileIds.Add(ToIdx(minX, minY));
            }
        }

        return true;
    }
}
