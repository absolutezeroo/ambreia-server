using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vortex.Logging;
using Vortex.Primitives;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Mapping;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Avatars;
using Vortex.Primitives.Rooms.Object.Furniture;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;

namespace Vortex.Rooms.Grains.Modules;

public sealed partial class RoomMapModule
{
    public async Task InvokeAvatarAsync(IRoomAvatar avatar, CancellationToken ct)
    {
        try
        {
            avatar.NeedsInvoke = false;

            if (avatar.IsWalking)
            {
                return;
            }

            int tileId = ToIdx(avatar.X, avatar.Y);
            RoomObjectId highestItemId = _roomGrain._state.TileHighestFloorItems[tileId];
            bool canSit = false;
            bool canLay = false;

            // Read before anything below moves the avatar, and announced once at the end. It used
            // to be captured inside the sit branch alone, so stepping OFF a seat — the two blocks
            // further down — resumed the dance and told nobody, which is the whole of "my dance
            // never comes back".
            AvatarDanceType previousDanceType =
                (avatar as IRoomPlayer)?.DanceType ?? AvatarDanceType.None;

            if (
                _roomGrain._state.ItemsById.TryGetValue(highestItemId, out IRoomItem? item)
                && item is IRoomFloorItem floorItem
            )
            {
                canSit = floorItem.Logic.CanSit();
                canLay = floorItem.Logic.CanLay();

                if (canSit)
                {
                    avatar.Sit(true, floorItem.Logic.GetPostureOffset(), floorItem.Rotation);
                }
                else if (canLay)
                {
                    avatar.Lay(true, floorItem.Logic.GetPostureOffset(), floorItem.Rotation);
                }

                await floorItem.Logic.OnInvokeAsync((IRoomAvatarContext)avatar.Logic.Context, ct);
            }

            if (!canSit && avatar.HasStatus(AvatarStatusType.Sit))
            {
                avatar.Sit(false);
            }

            if (!canLay && avatar.HasStatus(AvatarStatusType.Lay))
            {
                avatar.Lay(false);
            }

            _roomGrain.AvatarModule.BroadcastDanceIfChanged(avatar, previousDanceType);

            UpdateHeightForAvatar(avatar);
        }
        catch (Exception ex)
        {
            _roomGrain._logger.LogWarning(
                ex,
                "Failed to invoke avatar {ObjectId} in room {RoomId}.",
                avatar.ObjectId,
                _roomGrain.RoomId
            );
        }
    }

    public bool CanAvatarWalk(
        IRoomAvatar avatar,
        int tileIdx,
        bool isGoal = true,
        bool isDiagonalCheck = false
    )
    {
        if (!InBounds(tileIdx))
        {
            return false;
        }

        return CanAvatarOccupy(avatar, tileIdx, GetTopSection(tileIdx), isGoal, isDiagonalCheck);
    }

    /// <summary>
    /// The same question as <see cref="CanAvatarWalk" />, asked of a *named* surface rather than of
    /// whichever one happens to be on top.
    ///
    /// Disabled, Closed and the two occupancy flags belong to the column and are read off the tile.
    /// Walkable, Sittable and Layable belong to the surface and come from the section — which is
    /// the entire reason for the split: on a tile with two of them the top of a platform can be
    /// walkable while the seat beneath it is not.
    /// </summary>
    private bool CanAvatarOccupy(
        IRoomAvatar avatar,
        int tileIdx,
        RoomTileSection section,
        bool isGoal,
        bool isDiagonalCheck
    )
    {
        RoomTileFlags tileFlags = _roomGrain._state.TileFlags[tileIdx];

        if (tileFlags.Has(RoomTileFlags.Disabled) || tileFlags.Has(RoomTileFlags.Closed))
        {
            return false;
        }

        if (tileFlags.Has(RoomTileFlags.AvatarOccupied))
        {
            if (_roomGrain._state.TileAvatarStacks[tileIdx].Contains(avatar.ObjectId))
            {
                return true;
            }

            // RoomSnapshot.AllowBlocking is misnamed, and the name is what made this wrong: the
            // column is `allow_blocking` but what is stored in it is the client's *allowWalkThrough*
            // -- SaveRoomSettingsMessageHandler assigns message.AllowWalkThrough straight into it
            // and the outgoing serializer writes it straight back out, so the round trip is
            // consistent and only the reading of it was not. Tested as though it meant "blocking",
            // it did the exact opposite of the setting: a room with walk-through switched off, the
            // default, let everyone walk through everyone.
            //
            // ponytail: occupancy is per tile, not per section, so two avatars on two levels of the
            // same tile block each other. Splitting TileAvatarStacks by surface is the next thing to
            // do if standing under a platform ever needs to be shared.
            if (isGoal || !_roomGrain._state.RoomSnapshot.AllowBlocking)
            {
                return false;
            }
        }

        if (tileFlags.Has(RoomTileFlags.FurnitureOccupied))
        {
            bool isSeat = section.IsSittable || section.IsLayable;

            if (isSeat && (isDiagonalCheck || !isGoal))
            {
                return false;
            }

            if (isSeat && !isDiagonalCheck && isGoal)
            {
                return true;
            }

            // A bare surface under a raised item is walkable by definition — there is nothing on it
            // to refuse. Only a surface *formed* by furniture has to say it can be walked on, which
            // is what the flag means; without this test the floor beneath every platform would be
            // rejected for not being a walkable item.
            if (!section.IsBareFloor && !section.IsWalkable)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Whether a step from one tile to the next is walkable, and onto which surface.
    ///
    /// <paramref name="fromZ" /> is where the foot is now, and it is what makes this three
    /// dimensional: the destination is no longer "the tile" but whichever of the tile's surfaces is
    /// within a step of that altitude with room to stand. On a tile with a raised platform, an
    /// avatar walking along the floor is offered the floor and one walking along the platform is
    /// offered the platform — the same tile, two answers.
    ///
    /// The step-height limit lives inside that search rather than as a separate test: a surface out
    /// of reach is simply not returned, so a path that is planned is a path that can be walked.
    /// </summary>
    /// <summary>
    /// Every surface of the next tile this avatar may step onto from <paramref name="fromZ" />.
    ///
    /// The plural of <see cref="CanAvatarWalkBetween" />, and what the pathfinder asks: a tile is
    /// not one destination any more, and offering only its best one makes the walk one-way.
    /// </summary>
    public List<RoomTileSection> GetWalkableSectionsBetween(
        IRoomAvatar avatar,
        int pTileIdx,
        int nTileIdx,
        Altitude fromZ,
        bool isGoal = true
    )
    {
        List<RoomTileSection> sections = FindSections(
            nTileIdx,
            fromZ,
            Math.Abs(_roomGrain._roomConfig.MaxStepHeight)
        );

        sections.RemoveAll(section => !CanAvatarOccupy(avatar, nTileIdx, section, isGoal, false));

        if (sections.Count == 0 || !CornersAllowDiagonal(avatar, pTileIdx, nTileIdx))
        {
            return [];
        }

        return sections;
    }

    /// <summary>
    /// A diagonal step is refused when both tiles it cuts the corner of are blocked. Height plays
    /// no part in it, so it is asked once per step rather than once per surface.
    /// </summary>
    private bool CornersAllowDiagonal(IRoomAvatar avatar, int pTileIdx, int nTileIdx)
    {
        if (!_roomGrain._roomConfig.EnableDiagonalChecking || !IsDiagonal(pTileIdx, nTileIdx))
        {
            return true;
        }

        (int fromX, int fromY) = GetTileXY(pTileIdx);
        (int toX, int toY) = GetTileXY(nTileIdx);

        return CanAvatarWalk(avatar, ToIdx(toX, fromY), true, true)
            || CanAvatarWalk(avatar, ToIdx(fromX, toY), true, true);
    }

    public bool CanAvatarWalkBetween(
        IRoomAvatar avatar,
        int pTileIdx,
        int nTileIdx,
        Altitude fromZ,
        out RoomTileSection nextSection,
        bool isGoal = true
    )
    {
        nextSection = default;

        RoomTileSection? found = FindSection(
            nTileIdx,
            fromZ,
            Math.Abs(_roomGrain._roomConfig.MaxStepHeight)
        );

        if (found is null)
        {
            return false;
        }

        nextSection = found.Value;

        if (!CanAvatarOccupy(avatar, nTileIdx, nextSection, isGoal, false))
        {
            return false;
        }

        (int fromX, int fromY) = GetTileXY(pTileIdx);
        (int toX, int toY) = GetTileXY(nTileIdx);

        if (_roomGrain._roomConfig.EnableDiagonalChecking && IsDiagonal(pTileIdx, nTileIdx))
        {
            bool left = CanAvatarWalk(avatar, ToIdx(toX, fromY), true, true);
            bool right = CanAvatarWalk(avatar, ToIdx(fromX, toY), true, true);

            if (!left && !right)
            {
                return false;
            }
        }

        return true;
    }

    public bool RollAvatar(IRoomAvatar avatar, int tileIdx, Altitude z)
    {
        if (!InBounds(tileIdx))
        {
            throw new VortexException(VortexErrorCodeEnum.TileOutOfBounds);
        }

        RemoveAvatar(avatar, false);

        avatar.SetPosition(GetX(tileIdx), GetY(tileIdx));

        AddAvatar(avatar, false);

        avatar.SetHeight(z);

        return true;
    }

    public void AddAvatar(IRoomAvatar avatar, bool flush)
    {
        int tileIdx = ToIdx(avatar.X, avatar.Y);

        AddAvatarAtIdx(avatar, tileIdx, flush);
    }

    public void AddAvatarAtIdx(IRoomAvatar avatar, int tileIdx, bool flush)
    {
        if (!InBounds(tileIdx))
        {
            throw new VortexException(VortexErrorCodeEnum.TileOutOfBounds);
        }

        _roomGrain._state.TileAvatarStacks[tileIdx].Add(avatar.ObjectId);

        ComputeTile(tileIdx);

        if (flush) { }
    }

    public void RemoveAvatar(IRoomAvatar avatar, bool flush)
    {
        int tileIdx = ToIdx(avatar.X, avatar.Y);

        RemoveAvatarAtIdx(avatar, tileIdx, flush);
    }

    public void RemoveAvatarAtIdx(IRoomAvatar avatar, int tileIdx, bool flush)
    {
        if (!InBounds(tileIdx))
        {
            throw new VortexException(VortexErrorCodeEnum.TileOutOfBounds);
        }

        _roomGrain._state.TileAvatarStacks[tileIdx].Remove(avatar.ObjectId);

        ComputeTile(tileIdx);

        if (flush) { }
    }

    /// <summary>
    /// Settles the avatar onto the surface it just stepped onto.
    ///
    /// Called after <c>SetPosition()</c> and before <c>Z</c> has moved, so the avatar's own altitude
    /// is still the one it walked *from* — which is exactly the reference the section search needs.
    /// Snapping to the tile's top instead, as this did, is what would drop somebody walking under a
    /// platform onto the roof of it.
    /// </summary>
    public void UpdateHeightForAvatar(IRoomAvatar avatar)
    {
        try
        {
            int tileId = ToIdx(avatar.X, avatar.Y);
            RoomTileSection section =
                FindSection(tileId, avatar.Z, Math.Abs(_roomGrain._roomConfig.MaxStepHeight))
                ?? GetTopSection(tileId);
            Altitude height = section.Height;
            RoomObjectId highestItemId = section.ItemId;
            Altitude postureOffset = Altitude.Zero;

            if (highestItemId > 0)
            {
                if (
                    _roomGrain._state.ItemsById.TryGetValue(highestItemId, out IRoomItem? item)
                    && item is IRoomFloorItem floorItem
                )
                {
                    postureOffset = floorItem.Logic.GetPostureOffset();
                }
            }

            avatar.PostureOffset = postureOffset;

            avatar.SetHeight(height - postureOffset);
        }
        catch (Exception ex)
        {
            _roomGrain._logger.LogWarning(
                ex,
                "Failed to update height for avatar {ObjectId} in room {RoomId}.",
                avatar.ObjectId,
                _roomGrain.RoomId
            );
        }
    }

    /// <summary>
    /// How high an avatar stands on a tile: the surface, less whatever the item under it sinks the
    /// pose by (a chair seats you below its own top).
    ///
    /// Asked of the tile's section rather than of the flat arrays. One section today, so the same
    /// answer; when a tile has several this becomes "which surface", and every caller already
    /// passes a tile it means rather than a height it guessed.
    /// </summary>
    /// <summary>
    /// How high an avatar stands on a *given* surface: the surface itself, less whatever the item
    /// forming it sinks the pose by — a chair seats you below its own top.
    ///
    /// The arithmetic <see cref="GetTileHeightForAvatar" /> has always done, about a section the
    /// caller names rather than about whichever one is highest.
    /// </summary>
    public Altitude GetHeightForAvatarOn(RoomTileSection section)
    {
        if (
            section.ItemId > 0
            && _roomGrain._state.ItemsById.TryGetValue(section.ItemId, out IRoomItem? item)
            && item is IRoomFloorItem floorItem
        )
        {
            return section.Height - floorItem.Logic.GetPostureOffset();
        }

        return section.Height;
    }

    public Altitude GetTileHeightForAvatar(int tileId)
    {
        try
        {
            RoomTileSection section = GetTopSection(tileId);
            Altitude height = section.Height;
            RoomObjectId highestItemId = section.ItemId;
            Altitude postureOffset = Altitude.Zero;

            if (highestItemId > 0)
            {
                if (
                    _roomGrain._state.ItemsById.TryGetValue(highestItemId, out IRoomItem? item)
                    && item is IRoomFloorItem floorItem
                )
                {
                    postureOffset = floorItem.Logic.GetPostureOffset();
                }
            }

            return height - postureOffset;
        }
        catch (Exception ex)
        {
            _roomGrain._logger.LogWarning(
                ex,
                "Failed to get tile height for tile {TileId} in room {RoomId}.",
                tileId,
                _roomGrain.RoomId
            );

            return Altitude.Zero;
        }
    }
}
