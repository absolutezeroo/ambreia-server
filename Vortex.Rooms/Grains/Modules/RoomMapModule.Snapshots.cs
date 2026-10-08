using System;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Logging;
using Vortex.Primitives;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Snapshots.Mapping;

namespace Vortex.Rooms.Grains.Modules;

/// <summary>The map as the rest of the hotel reads it: the whole-map snapshot, one tile, and the
/// height encoding the client expects.</summary>
public sealed partial class RoomMapModule
{
    public RoomMapSnapshot GetMapSnapshot(CancellationToken ct)
    {
        if (_dirty || _mapSnapshot is null)
        {
            _mapSnapshot = BuildSnapshot();
            _dirty = false;
        }

        return _mapSnapshot;
    }

    public Task<RoomTileSnapshot> GetTileSnapshotAsync(int x, int y, CancellationToken ct)
    {
        // Here rather than only in the id overload below, because ToIdx destroys the evidence: x =
        // Width folds onto the first tile of the next row, and the id that comes out is a perfectly
        // legal one. A coordinate pair can only be bounds-checked as a pair.
        if (!InBounds(x, y))
        {
            throw new VortexException(VortexErrorCodeEnum.TileOutOfBounds);
        }

        return GetTileSnapshotAsync(ToIdx(x, y), ct);
    }

    public Task<RoomTileSnapshot> GetTileSnapshotAsync(int id, CancellationToken ct)
    {
        // Both overloads of this are members of IRoomMap, a public grain interface, and this one
        // indexes five parallel arrays with the number it is handed. Every caller in the repository
        // passes an object's own position, but that is a fact about today's callers rather than a
        // property of the method (ROOMM-TILECALL-043).
        if (!InBounds(id))
        {
            throw new VortexException(VortexErrorCodeEnum.TileOutOfBounds);
        }

        return Task.FromResult(
            new RoomTileSnapshot
            {
                X = (byte)(id % (_roomGrain._state.Model?.Width ?? 0)),
                Y = (byte)(id / (_roomGrain._state.Model?.Width ?? 0)),
                Height = _roomGrain._state.TileHeights[id],
                EncodedHeight = _roomGrain._state.TileEncodedHeights[id],
                Flags = _roomGrain._state.TileFlags[id],
                HighestObjectId = _roomGrain._state.TileHighestFloorItems[id],
                FloorObjectIds = [.. _roomGrain._state.TileFloorStacks[id]],
                AvatarObjectIds = [.. _roomGrain._state.TileAvatarStacks[id]],
            }
        );
    }

    private RoomMapSnapshot BuildSnapshot()
    {
        return new RoomMapSnapshot
        {
            ModelName = _roomGrain._state.Model?.Name ?? string.Empty,
            ModelData = _roomGrain._state.Model?.Model ?? string.Empty,
            Width = _roomGrain._state.Model?.Width ?? 0,
            Height = _roomGrain._state.Model?.Height ?? 0,
            Size = _roomGrain._state.Model?.Size ?? 0,
            DoorX = _roomGrain._state.Model?.DoorX ?? 0,
            DoorY = _roomGrain._state.Model?.DoorY ?? 0,
            DoorRotation = _roomGrain._state.Model?.DoorRotation ?? 0,
            TileEncodedHeights = [.. _roomGrain._state.TileEncodedHeights],
        };
    }

    private static short EncodeHeight(Altitude height, bool stackingBlocked)
    {
        if (height < Altitude.Zero || stackingBlocked)
        {
            return -1;
        }

        int stackingMask = 1 << 14;
        int heightMask = stackingMask - 1;
        int raw = (int)Math.Round(height.Value * 256.0);

        if (raw < 0)
        {
            raw = 0;
        }

        if (raw > heightMask)
        {
            raw = heightMask;
        }

        int value = raw;

        value &= 0x7FFF;

        return unchecked((short)value);
    }
}
