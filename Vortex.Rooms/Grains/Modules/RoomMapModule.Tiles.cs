using System.Collections.Generic;
using System.Collections.Immutable;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Mapping;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Avatars;
using Vortex.Primitives.Rooms.Object.Furniture;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Primitives.Rooms.Object.Logic;
using Vortex.Rooms.Object.Logic.Furniture.Floor;

namespace Vortex.Rooms.Grains.Modules;

/// <summary>What stands on a tile and what it allows: the computed flags and height, the surfaces
/// an avatar can step onto, where one may appear.</summary>
public sealed partial class RoomMapModule
{
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

    /// <summary>
    /// What a floor item's top allows: stack on it, walk, sit, lay. Asked of the topmost item by
    /// <see cref="ComputeTile(int)"/> and of each item by <see cref="CollectOccupants"/> -- which is
    /// what lets two surfaces of one tile disagree about what you may do on them.
    /// </summary>
    private RoomTileFlags SurfaceFlags(IRoomFloorItem item)
    {
        RoomTileFlags flags = RoomTileFlags.None;

        if (!item.Logic.CanStack())
        {
            flags = flags.Add(RoomTileFlags.StackBlocked);
        }

        if (CanWalkOn(item))
        {
            flags = flags.Add(RoomTileFlags.Walkable);
        }

        if (CanSitOn(item))
        {
            flags = flags.Add(RoomTileFlags.Sittable);
        }

        if (CanLayOn(item))
        {
            flags = flags.Add(RoomTileFlags.Layable);
        }

        return flags;
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

                if (item.Logic is FurnitureInvisibleClickTileLogic)
                {
                    nextFlags = nextFlags.Add(RoomTileFlags.TileClickListener);
                }

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
            nextFlags = nextFlags.Add(SurfaceFlags(nextHighestItem));
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
    public RoomTileSection? FindSection(
        int tileId,
        Altitude fromZ,
        Altitude maxStep,
        Altitude? maxDrop = null
    )
    {
        if (!InBounds(tileId))
        {
            return null;
        }

        Altitude floorHeight = _roomGrain._state.Model?.BaseHeights[tileId] ?? Altitude.Zero;

        return RoomTileSectionFinder.Find(
            floorHeight,
            CollectOccupants(tileId),
            fromZ,
            maxStep,
            maxDrop
        );
    }

    /// <summary>
    /// Every surface of the tile an avatar at <paramref name="fromZ" /> could step onto, not just
    /// the best one.
    ///
    /// The pathfinder needs all of them. Offered only the highest, a walk can climb onto a platform
    /// and never step down off it — the low surface is never proposed while a high one is also
    /// within a step, so going up worked and coming down did not.
    /// </summary>
    public List<RoomTileSection> FindSections(
        int tileId,
        Altitude fromZ,
        Altitude maxStep,
        Altitude? maxDrop = null
    )
    {
        if (!InBounds(tileId))
        {
            return [];
        }

        Altitude floorHeight = _roomGrain._state.Model?.BaseHeights[tileId] ?? Altitude.Zero;

        return RoomTileSectionFinder.FindAll(
            floorHeight,
            CollectOccupants(tileId),
            fromZ,
            maxStep,
            maxDrop
        );
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

            occupants.Add(
                new RoomTileOccupant
                {
                    ItemId = floorItem.ObjectId,
                    Bottom = floorItem.Z,
                    Top = floorItem.Height,
                    Flags = SurfaceFlags(floorItem),
                }
            );
        }

        return [.. occupants];
    }
}
