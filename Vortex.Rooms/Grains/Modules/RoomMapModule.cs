using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Logging;
using Vortex.Primitives;
using Vortex.Primitives.Action;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Events.Player;
using Vortex.Primitives.Rooms.Mapping;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Avatars;
using Vortex.Primitives.Rooms.Object.Furniture;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Primitives.Rooms.Object.Furniture.Wall;
using Vortex.Primitives.Rooms.Object.Logic;
using Vortex.Primitives.Rooms.Snapshots.Mapping;
using Vortex.Rooms.Object.Logic.Furniture.Floor;

namespace Vortex.Rooms.Grains.Modules;

public sealed partial class RoomMapModule(RoomGrain roomGrain)
{
    private readonly RoomGrain _roomGrain = roomGrain;
    private bool _dirty = true;

    private RoomMapSnapshot? _mapSnapshot;

    public int Width => _roomGrain._state.Model?.Width ?? 0;
    public int Height => _roomGrain._state.Model?.Height ?? 0;
    public int Size => _roomGrain._state.Model?.Size ?? 0;

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

    /// <summary>The first floor item on the tile whose logic is a <typeparamref name="TLogic"/>, or
    /// null. Bounds-checks the index, so callers can pass a raw <see cref="ToIdx"/> result.</summary>
    public TLogic? FirstLogicOnTile<TLogic>(int tileIdx)
        where TLogic : class, IRoomObjectLogic
    {
        if (tileIdx < 0 || tileIdx >= _roomGrain._state.TileFloorStacks.Length)
        {
            return null;
        }

        foreach (RoomObjectId id in _roomGrain._state.TileFloorStacks[tileIdx])
        {
            if (
                _roomGrain._state.ItemsById.TryGetValue(id, out IRoomItem? item)
                && item.Logic is TLogic logic
            )
            {
                return logic;
            }
        }

        return null;
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

    /// <summary>
    /// Picks a spawn tile when the model's door coordinates cannot be used (malformed model whose
    /// door falls outside the map). Scans the map in index order and returns the first tile the
    /// room considers enterable, preferring one that nothing is standing on; falls back to any
    /// enterable tile, then to (0, 0) when the model has none at all.
    /// </summary>
    public (int x, int y) FindFallbackSpawnTile()
    {
        int size = Size;
        int occupiedIdx = -1;

        for (int idx = 0; idx < size; idx++)
        {
            if (!IsSpawnable(idx))
            {
                continue;
            }

            if (!GetTileFlagsOrBase(idx).Has(RoomTileFlags.AvatarOccupied))
            {
                return (GetX(idx), GetY(idx));
            }

            if (occupiedIdx < 0)
            {
                occupiedIdx = idx;
            }
        }

        return occupiedIdx >= 0 ? (GetX(occupiedIdx), GetY(occupiedIdx)) : (0, 0);
    }

    /// <summary>A zone's door or collision override first, else the furni's own logic.</summary>
    public bool CanWalkOn(IRoomFloorItem item) =>
        _roomGrain._state.ZoneRuntime?.Walkable(item.ObjectId.Value) ?? item.Logic.CanWalk();

    public bool CanSitOn(IRoomFloorItem item) =>
        _roomGrain._state.ZoneRuntime?.Seat(item.ObjectId.Value) ?? item.Logic.CanSit();

    public bool CanLayOn(IRoomFloorItem item) =>
        _roomGrain._state.ZoneRuntime?.Bed(item.ObjectId.Value) ?? item.Logic.CanLay();

    /// <summary>Whether an avatar may appear on this tile, whoever already stands there.</summary>
    public bool IsSpawnable(int idx)
    {
        RoomTileFlags flags = GetTileFlagsOrBase(idx);

        if (flags.Has(RoomTileFlags.Disabled) || flags.Has(RoomTileFlags.Closed))
        {
            return false;
        }

        // Mirrors CanAvatarWalk with isGoal: a sittable/layable tile is a legal destination,
        // any other furniture-occupied tile is only enterable when the stack is walkable.
        return !flags.Has(RoomTileFlags.FurnitureOccupied)
            || flags.Has(RoomTileFlags.Walkable)
            || flags.Has(RoomTileFlags.Sittable)
            || flags.Has(RoomTileFlags.Layable);
    }

    /// <summary>
    /// Reads live tile flags, falling back to the model's base flags when the live map has not been
    /// built yet, and to a closed tile when neither is available.
    /// </summary>
    private RoomTileFlags GetTileFlagsOrBase(int idx)
    {
        RoomTileFlags[] liveFlags = _roomGrain._state.TileFlags;

        if ((uint)idx < (uint)liveFlags.Length)
        {
            return liveFlags[idx];
        }

        RoomTileFlags[]? baseFlags = _roomGrain._state.Model?.BaseFlags;

        return baseFlags is not null && (uint)idx < (uint)baseFlags.Length
            ? baseFlags[idx]
            : RoomTileFlags.Disabled | RoomTileFlags.Closed;
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
        // checked on the coordinates, not on the flattened index -- see GetTileIdForSize below for
        // the worked counter-example: x = Width folds onto the next row, which would let a player
        // fire the click listener of a tile they never clicked, from anywhere in the room.
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

    public void ComputeAllTiles()
    {
        for (int idx = 0; idx < Size; idx++)
        {
            ComputeTile(idx);
        }
    }

    public void ComputeTile(int x, int y)
    {
        ComputeTile(ToIdx(x, y));
    }

    public void ComputeTile(int id)
    {
        if (_roomGrain._state.IsTileComputationPaused || !InBounds(id))
        {
            return;
        }

        Altitude nextHeight = _roomGrain._state.Model?.BaseHeights[id] ?? 0.0;
        RoomTileFlags nextFlags =
            _roomGrain._state.Model?.BaseFlags[id] ?? RoomTileFlags.Disabled | RoomTileFlags.Closed;
        HashSet<RoomObjectId> floorStack = _roomGrain._state.TileFloorStacks[id];
        HashSet<RoomObjectId> avatarStack = _roomGrain._state.TileAvatarStacks[id];

        IRoomFloorItem? nextHighestItem = null;

        if (avatarStack.Count > 0)
        {
            nextFlags = nextFlags.Add(RoomTileFlags.AvatarOccupied);

            foreach (RoomObjectId objectId in avatarStack)
            {
                if (
                    !_roomGrain._state.AvatarsByObjectId.TryGetValue(
                        objectId,
                        out IRoomAvatar? avatar
                    )
                )
                {
                    continue;
                }

                avatar.NeedsInvoke = true;
            }
        }

        if (floorStack.Count > 0)
        {
            nextFlags = nextFlags.Add(RoomTileFlags.FurnitureOccupied);

            foreach (RoomObjectId itemId in floorStack)
            {
                if (!_roomGrain._state.ItemsById.TryGetValue(itemId, out IRoomItem? item))
                {
                    continue;
                }

                Altitude height = item.Height;

                if (item.Logic is FurnitureInvisibleClickTileLogic invisLogic)
                {
                    nextFlags = nextFlags.Add(RoomTileFlags.TileClickListener);
                }

                // special logic if stack helper

                if (height < nextHeight)
                {
                    continue;
                }

                nextHeight = height;
                nextHighestItem = (IRoomFloorItem)item;
            }
        }

        if (nextHighestItem is not null)
        {
            if (!nextHighestItem.Logic.CanStack())
            {
                nextFlags = nextFlags.Add(RoomTileFlags.StackBlocked);
            }

            if (CanWalkOn(nextHighestItem))
            {
                nextFlags = nextFlags.Add(RoomTileFlags.Walkable);
            }

            if (CanSitOn(nextHighestItem))
            {
                nextFlags = nextFlags.Add(RoomTileFlags.Sittable);
            }

            if (CanLayOn(nextHighestItem))
            {
                nextFlags = nextFlags.Add(RoomTileFlags.Layable);
            }
        }

        _roomGrain._state.TileHeights[id] = nextHeight;
        _roomGrain._state.TileFlags[id] = nextFlags;
        _roomGrain._state.TileHighestFloorItems[id] = nextHighestItem?.ObjectId ?? -1;

        short prevEncoded = _roomGrain._state.TileEncodedHeights[id];
        short nextEncoded = EncodeHeight(nextHeight, nextFlags.Has(RoomTileFlags.StackBlocked));

        if (prevEncoded != nextEncoded)
        {
            _roomGrain._state.TileEncodedHeights[id] = nextEncoded;
            _roomGrain._state.DirtyHeightTileIds.Add(id);
        }

        _dirty = true;
    }

    /// <summary>
    /// The tiles furniture occupies, for a future map editor to treat as reserved; nothing calls it
    /// yet. Read off the live flags rather than
    /// recomputed from item footprints, so a two-by-two rug reserves all four of its tiles and a
    /// rotated item reserves the tiles it actually covers — the room engine already worked that out
    /// when the item was placed.
    /// </summary>
    public ImmutableArray<(int X, int Y)> GetOccupiedTiles()
    {
        int size = Size;

        if (size <= 0 || Width <= 0)
        {
            return [];
        }

        ImmutableArray<(int X, int Y)>.Builder tiles = ImmutableArray.CreateBuilder<(
            int X,
            int Y
        )>();

        for (int idx = 0; idx < size; idx++)
        {
            if (GetTileFlagsOrBase(idx).Has(RoomTileFlags.FurnitureOccupied))
            {
                tiles.Add((GetX(idx), GetY(idx)));
            }
        }

        return tiles.ToImmutable();
    }

    /// <summary>
    /// The walkable surface of a tile, as a section.
    ///
    /// Derived rather than stored: the three flat arrays already hold this tile's one surface, and
    /// building a section out of them costs nothing (it is a struct) while giving every caller an
    /// API whose answer can grow to several. That is the whole of the first step — no behaviour
    /// moves, and the storage can be replaced later without any of these callers changing again.
    ///
    /// Out of range answers a bare, unwalkable floor at zero rather than throwing: the callers that
    /// come next are pathfinding probes, for which "nothing here" is the useful answer.
    ///
    /// The range is the *array's*, not <c>InBounds()</c>'s. Those two are not the same thing and the
    /// difference is not theoretical: InBounds() asks the model's dimensions, while these arrays are
    /// allocated by EnsureMapBuiltAsync(), so a room whose model is loaded and whose map is not yet
    /// built passes the first test and throws on the second.
    /// </summary>
    public RoomTileSection GetTopSection(int tileId)
    {
        if ((uint)tileId >= (uint)_roomGrain._state.TileHeights.Length)
        {
            return new RoomTileSection
            {
                Height = Altitude.Zero,
                ItemId = -1,
                Flags = RoomTileFlags.None,
            };
        }

        return new RoomTileSection
        {
            Height = _roomGrain._state.TileHeights[tileId],
            ItemId = _roomGrain._state.TileHighestFloorItems[tileId],
            Flags = _roomGrain._state.TileFlags[tileId] & RoomTileSection.SectionFlags,
        };
    }

    /// <summary>
    /// The surface an avatar standing at <paramref name="fromZ" /> could step onto on this tile.
    ///
    /// Unlike <see cref="GetTopSection" />, which reports the one surface the tile has always had,
    /// this considers every item on it — so the floor *under* a raised platform is an answer, and a
    /// tile is no longer walkable-or-not but walkable-at-some-height.
    ///
    /// The best surface only: asked to settle an avatar's height, to tell a clicked tile's surfaces
    /// apart, and by the step check between tiles. The pathfinder wants
    /// every surface and asks <see cref="FindSections" />.
    /// </summary>
    public RoomTileSection? FindSection(int tileId, Altitude fromZ, Altitude maxStep)
    {
        if (!InBounds(tileId))
        {
            return null;
        }

        Altitude floorHeight = _roomGrain._state.Model?.BaseHeights[tileId] ?? Altitude.Zero;

        return RoomTileSectionFinder.Find(floorHeight, CollectOccupants(tileId), fromZ, maxStep);
    }

    /// <summary>
    /// Every surface of the tile an avatar at <paramref name="fromZ" /> could step onto, not just
    /// the best one.
    ///
    /// The pathfinder needs all of them. Offered only the highest, a walk can climb onto a platform
    /// and never step down off it — the low surface is never proposed while a high one is also
    /// within a step, so going up worked and coming down did not.
    /// </summary>
    public List<RoomTileSection> FindSections(int tileId, Altitude fromZ, Altitude maxStep)
    {
        if (!InBounds(tileId))
        {
            return [];
        }

        Altitude floorHeight = _roomGrain._state.Model?.BaseHeights[tileId] ?? Altitude.Zero;

        return RoomTileSectionFinder.FindAll(floorHeight, CollectOccupants(tileId), fromZ, maxStep);
    }

    /// <summary>
    /// Flattens the tile's floor stack into the vertical slabs the finder reasons about.
    ///
    /// Every item, not the highest one: that is the entire difference between this and
    /// <c>ComputeTile()</c>, which reads the same stack and throws all but the top of it away.
    /// </summary>
    private RoomTileOccupant[] CollectOccupants(int tileId)
    {
        HashSet<RoomObjectId> stack = _roomGrain._state.TileFloorStacks[tileId];

        if (stack.Count == 0)
        {
            return [];
        }

        List<RoomTileOccupant> occupants = new(stack.Count);

        foreach (RoomObjectId itemId in stack)
        {
            if (
                !_roomGrain._state.ItemsById.TryGetValue(itemId, out IRoomItem? item)
                || item is not IRoomFloorItem floorItem
            )
            {
                continue;
            }

            RoomTileFlags flags = RoomTileFlags.None;

            // The same four questions ComputeTile() asks of the topmost item, asked of each --
            // which is what lets two surfaces of one tile disagree about what you may do on them.
            if (!floorItem.Logic.CanStack())
            {
                flags = flags.Add(RoomTileFlags.StackBlocked);
            }

            if (CanWalkOn(floorItem))
            {
                flags = flags.Add(RoomTileFlags.Walkable);
            }

            if (CanSitOn(floorItem))
            {
                flags = flags.Add(RoomTileFlags.Sittable);
            }

            if (CanLayOn(floorItem))
            {
                flags = flags.Add(RoomTileFlags.Layable);
            }

            occupants.Add(
                new RoomTileOccupant
                {
                    ItemId = floorItem.ObjectId,
                    Bottom = floorItem.Z,
                    Top = floorItem.Height,
                    Flags = flags,
                }
            );
        }

        return [.. occupants];
    }

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
