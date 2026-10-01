using System.Collections.Generic;
using Vortex.Logging;
using Vortex.Primitives;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;

namespace Vortex.Rooms.Grains.Modules;

public sealed partial class RoomMapModule
{
    private bool AddFloorItem(IRoomFloorItem item)
    {
        int tileIdx = ToIdx(item.X, item.Y);

        if (!InBounds(tileIdx))
        {
            throw new VortexException(VortexErrorCodeEnum.TileOutOfBounds);
        }

        if (
            GetTileIdForSize(
                item.X,
                item.Y,
                item.Rotation,
                item.Definition.Width,
                item.Definition.Length,
                out List<int> tileIds
            )
        )
        {
            foreach (int idx in tileIds)
            {
                _roomGrain._state.TileFloorStacks[idx].Add(item.ObjectId);

                ComputeTile(idx);
            }
        }

        return true;
    }

    /// <summary>
    /// Gives the item its resting place on <paramref name="nTileIdx" />, without registering it.
    /// </summary>
    /// <remarks>
    /// Positioning and registering are two steps on purpose, and the order between them is the
    /// point: <c>AddFloorItem</c> reads the item's own coordinates, so it can only run once the
    /// item has them. This used to do both, after the caller had already attached the item — which
    /// registered the footprint at the default (0, 0) first, and left it there for good, because
    /// removal only ever clears the footprint the item is standing on.
    ///
    /// The Z read here is the tile's height before this item joins the pile, which is what makes it
    /// the surface the item rests on rather than its own top.
    /// </remarks>
    public void PositionFloorItem(IRoomFloorItem item, int nTileIdx, Rotation rot)
    {
        if (!InBounds(nTileIdx))
        {
            throw new VortexException(VortexErrorCodeEnum.TileOutOfBounds);
        }

        (int targetX, int targetY) = GetTileXY(nTileIdx);

        item.SetPosition(targetX, targetY);
        item.SetPositionZ(_roomGrain._state.TileHeights[nTileIdx]);
        item.SetRotation(rot);
    }

    /// <summary>
    /// Positions the item and registers the footprint it now stands on, in that order.
    /// </summary>
    /// <remarks>
    /// For an item already live in the room whose footprint has just been taken off the map. An
    /// item being attached must not come through here -- <c>AttatchObjectAsync</c> registers it, so
    /// it would be registered twice; that path positions first and lets the attach do the rest.
    /// </remarks>
    public bool PlaceFloorItem(IRoomFloorItem item, int nTileIdx, Rotation rot)
    {
        PositionFloorItem(item, nTileIdx, rot);

        return AddFloorItem(item);
    }

    /// <param name="restackInPlace">
    /// True for a player turning an item where it stands: it is measured once it is off its own
    /// tiles, so an item turned under a stack lands on top of it, as the hotel does. Wired turns keep
    /// the height — a rotating piece at the foot of a build would otherwise climb it every tick.
    /// </param>
    public bool MoveFloorItem(
        IRoomFloorItem item,
        int tileIdx,
        Altitude? z = null,
        Rotation? targetRot = null,
        bool restackInPlace = false
    )
    {
        if (!InBounds(tileIdx))
        {
            throw new VortexException(VortexErrorCodeEnum.TileOutOfBounds);
        }

        (int sourceX, int sourceY, Rotation sourceRot) = (item.X, item.Y, item.Rotation);
        (int targetX, int targetY) = GetTileXY(tileIdx);
        bool samePlace = sourceX == targetX && sourceY == targetY;
        Altitude finalZ = z ?? (samePlace ? item.Z : _roomGrain._state.TileHeights[tileIdx]);
        Rotation finalRot = targetRot ?? sourceRot;

        if (!samePlace || sourceRot != finalRot)
        {
            RemoveFloorItem(item);

            if (z is null && samePlace && restackInPlace)
            {
                finalZ = _roomGrain._state.TileHeights[tileIdx];
            }

            item.SetPosition(targetX, targetY);
            item.SetPositionZ(finalZ);
            item.SetRotation(finalRot);

            AddFloorItem(item);
        }
        else
        {
            item.SetPositionZ(finalZ);

            RecomputeFootprint(item);
        }

        return true;
    }

    /// <summary>
    /// Recomputes every tile the item stands on, after something about the item changed without it
    /// moving.
    /// </summary>
    /// <remarks>
    /// A tile's height and its walk/sit/lay flags are derived from the furniture standing on it, so
    /// anything that changes an item's height or solidity invalidates its whole footprint -- not the
    /// tile its coordinates name. For a 1x1 those are the same tile, which is why recomputing just
    /// the one survived: the families where they differ are the 2x2, 4x4 and larger, and the stale
    /// tiles keep the height and flags they had before the change. The visible version is walking
    /// through the far half of a gate that just closed.
    /// </remarks>
    public void RecomputeFootprint(IRoomFloorItem item)
    {
        if (
            !GetTileIdForSize(
                item.X,
                item.Y,
                item.Rotation,
                item.Definition.Width,
                item.Definition.Length,
                out List<int> tileIds
            )
        )
        {
            return;
        }

        foreach (int idx in tileIds)
        {
            ComputeTile(idx);
        }
    }

    public bool RollFloorItem(IRoomFloorItem item, int tileIdx, Altitude z)
    {
        if (!InBounds(tileIdx))
        {
            throw new VortexException(VortexErrorCodeEnum.TileOutOfBounds);
        }

        RemoveFloorItem(item);

        (int targetX, int targetY) = GetTileXY(tileIdx);

        item.SetPosition(targetX, targetY);
        item.SetPositionZ(z);

        AddFloorItem(item);

        return true;
    }

    public bool RemoveFloorItem(IRoomFloorItem item)
    {
        int tileIdx = ToIdx(item.X, item.Y);

        if (!InBounds(tileIdx))
        {
            throw new VortexException(VortexErrorCodeEnum.TileOutOfBounds);
        }

        if (
            GetTileIdForSize(
                item.X,
                item.Y,
                item.Rotation,
                item.Definition.Width,
                item.Definition.Length,
                out List<int> tileIds
            )
        )
        {
            foreach (int idx in tileIds)
            {
                _roomGrain._state.TileFloorStacks[idx].Remove(item.ObjectId);

                ComputeTile(idx);
            }
        }

        return true;
    }
}
