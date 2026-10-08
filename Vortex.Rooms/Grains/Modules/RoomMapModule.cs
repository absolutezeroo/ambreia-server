using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Action;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Events.Player;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Furniture;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Primitives.Rooms.Object.Furniture.Wall;
using Vortex.Primitives.Rooms.Snapshots.Mapping;

namespace Vortex.Rooms.Grains.Modules;

/// <summary>
/// The room's map: the tile arrays built from the model, the items and avatars placed on them, and
/// a click on a tile. Its parts: coordinates (<c>RoomMapModule.Geometry.cs</c>), what a tile allows
/// (<c>.Tiles.cs</c>), what the hotel reads of it (<c>.Snapshots.cs</c>), and placing avatars,
/// floor items and wall items (<c>.Avatar.cs</c>, <c>.Floor.cs</c>, <c>.Wall.cs</c>).
/// </summary>
public sealed partial class RoomMapModule(RoomGrain roomGrain)
{
    private readonly RoomGrain _roomGrain = roomGrain;
    private bool _dirty = true;

    private RoomMapSnapshot? _mapSnapshot;

    public int Width => _roomGrain._state.Model?.Width ?? 0;
    public int Height => _roomGrain._state.Model?.Height ?? 0;
    public int Size => _roomGrain._state.Model?.Size ?? 0;

    public bool AddItem(IRoomItem item)
    {
        return item switch
        {
            IRoomFloorItem floor => AddFloorItem(floor),
            IRoomWallItem wall => AddWallItem(wall),
            _ => false,
        };
    }

    public bool RemoveItem(IRoomItem item)
    {
        return item switch
        {
            IRoomFloorItem floor => RemoveFloorItem(floor),
            IRoomWallItem wall => RemoveWallItem(wall),
            _ => false,
        };
    }

    public Task ClickTileAsync(ActionContext ctx, int x, int y, CancellationToken ct)
    {
        // These are raw ints off the wire, on the most-sent packet of the game. Bounds have to be
        // checked on the coordinates, not on the flattened index -- see GetTileIdForSize for the
        // worked counter-example: x = Width folds onto the next row, which would let a player fire
        // the click listener of a tile they never clicked, from anywhere in the room.
        if (!InBounds(x, y))
        {
            return Task.CompletedTask;
        }

        int idx = ToIdx(x, y);
        RoomTileFlags tile = _roomGrain._state.TileFlags[idx];

        if (!tile.Has(RoomTileFlags.TileClickListener))
        {
            return Task.CompletedTask;
        }

        return _roomGrain.PublishRoomEventAsync(
            new PlayerClickedTileEvent
            {
                PlayerId = ctx.PlayerId,
                TileX = x,
                TileY = y,
                RoomId = _roomGrain.RoomId,
                CausedBy = ctx,
            },
            ct
        );
    }

    internal Task EnsureMapBuiltAsync(CancellationToken ct)
    {
        if (!_roomGrain._state.IsMapReady)
        {
            int size = _roomGrain._state.Model?.Size ?? 0;

            Altitude[] tileHeights = new Altitude[size];
            short[] tileEncodedHeights = new short[size];
            RoomTileFlags[] tileFlags = new RoomTileFlags[size];
            RoomObjectId[] tileHighestFloorItems = new RoomObjectId[size];
            HashSet<RoomObjectId>[] tileFloorStacks = new HashSet<RoomObjectId>[size];
            HashSet<RoomObjectId>[] tileAvatarStacks = new HashSet<RoomObjectId>[size];

            for (int id = 0; id < size; id++)
            {
                Altitude height = _roomGrain._state.Model?.BaseHeights[id] ?? Altitude.Zero;
                RoomTileFlags flags =
                    _roomGrain._state.Model?.BaseFlags[id]
                    ?? RoomTileFlags.Disabled | RoomTileFlags.Closed | RoomTileFlags.StackBlocked;

                tileHeights[id] = height;
                tileEncodedHeights[id] = EncodeHeight(
                    height,
                    flags.Has(RoomTileFlags.StackBlocked)
                );
                tileFlags[id] = flags;
                tileHighestFloorItems[id] = -1;
                tileFloorStacks[id] = [];
                tileAvatarStacks[id] = [];
            }

            _roomGrain._state.TileHeights = tileHeights;
            _roomGrain._state.TileEncodedHeights = tileEncodedHeights;
            _roomGrain._state.TileFlags = tileFlags;
            _roomGrain._state.TileHighestFloorItems = tileHighestFloorItems;
            _roomGrain._state.TileFloorStacks = tileFloorStacks;
            _roomGrain._state.TileAvatarStacks = tileAvatarStacks;
            _roomGrain._state.IsMapReady = true;
        }

        return Task.CompletedTask;
    }
}
