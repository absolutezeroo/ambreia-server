using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vortex.Primitives.Action;
using Vortex.Primitives.Bots;
using Vortex.Primitives.Furniture.Snapshots.StuffData;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Enums.Wired;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Avatars;
using Vortex.Primitives.Rooms.Object.Furniture;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Primitives.Rooms.Object.Furniture.Wall;
using Vortex.Primitives.Rooms.Snapshots.Wired;
using Vortex.Primitives.Rooms.Wired;
using Vortex.Rooms.Wired.Engine;

namespace Vortex.Rooms.Wired;

internal sealed class WiredExecutionContext(IWiredRoomHost host)
    : WiredContext(host),
        IWiredExecutionContext
{
    private IWiredRoomActions Actions => _host.Actions;

    private IWiredRoomView Room => _host.View;

    private IWiredDiagnostics Diagnostics => _host.Diagnostics;

    public List<IWiredAddon> Addons { get; init; } = [];

    public List<WiredUserMovementSnapshot> UserMoves { get; } = [];
    public List<WiredFloorItemMovementSnapshot> FloorItemMoves { get; } = [];
    public List<WiredWallItemMovementSnapshot> WallItemMoves { get; } = [];
    public List<WiredUserDirectionSnapshot> UserDirections { get; } = [];
    public List<(RoomObjectId, StuffDataSnapshot)> FloorItemStateUpdates { get; } = [];
    public List<(RoomObjectId, string)> WallItemStateUpdates { get; } = [];

    public async Task ProcessItemStateUpdateAsync(IRoomItem item, int state)
    {
        if (item is null)
        {
            return;
        }

        try
        {
            await item.Logic.SetStateAsync(state, false);

            switch (item)
            {
                case IRoomFloorItem:
                    FloorItemStateUpdates.Add((item.ObjectId, item.Logic.StuffData.GetSnapshot()));
                    break;
                case IRoomWallItem:
                    WallItemStateUpdates.Add((item.ObjectId, item.Logic.GetLegacyString()));
                    break;
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Logger.LogWarning(
                ex,
                "Failed to process wired state update ({State}) for item {ItemId}.",
                state,
                item.ObjectId
            );
        }
    }

    public async Task ProcessFloorItemMovementAsync(
        IRoomFloorItem floorItem,
        int tileIdx,
        Altitude? z,
        Rotation? rot
    )
    {
        if (floorItem is null)
        {
            return;
        }

        try
        {
            (int sourceX, int sourceY, Altitude sourceZ) = (floorItem.X, floorItem.Y, floorItem.Z);
            (int targetX, int targetY) = Actions.GetTileXY(tileIdx);

            Rotation? addonRot = null;

            foreach (IWiredAddon addon in Addons)
            {
                addonRot =
                    await addon.RotateFloorItemMoveAsync(
                        floorItem,
                        targetX,
                        targetY,
                        this,
                        CancellationToken.None
                    ) ?? addonRot;
            }

            // A rotation an add-on asked for is cosmetic; the move is what the action promised. A
            // furni that cannot take it (a two-direction item asked to face a diagonal) still moves.
            bool moved =
                (addonRot is not null && Actions.MoveFloorItem(floorItem, tileIdx, z, addonRot))
                || Actions.MoveFloorItem(floorItem, tileIdx, z, rot);

            if (moved)
            {
                WiredFloorItemMovementSnapshot move = new()
                {
                    ObjectId = floorItem.ObjectId,
                    SourceX = sourceX,
                    SourceY = sourceY,
                    SourceZ = sourceZ,
                    TargetX = floorItem.X,
                    TargetY = floorItem.Y,
                    TargetZ = floorItem.Z,
                    Rotation = floorItem.Rotation,
                    AnimationTime =
                        Policy.AnimationMode == WiredAnimationModeType.Instant
                            ? 0
                            : Policy.AnimationTimeMs,
                };

                foreach (IWiredAddon addon in Addons)
                {
                    move = await addon.ShapeFloorItemMoveAsync(
                        floorItem,
                        move,
                        this,
                        CancellationToken.None
                    );
                }

                FloorItemMoves.Add(move);
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Logger.LogWarning(
                ex,
                "Failed to process wired floor-item movement for item {ItemId} to tile {TileIdx}.",
                floorItem.ObjectId,
                tileIdx
            );
        }
    }

    public Task ProcessWallItemMovementAsync(
        IRoomWallItem wallItem,
        int x,
        int y,
        Altitude z,
        Rotation rot,
        int wallOffset
    )
    {
        if (wallItem is null)
        {
            return Task.CompletedTask;
        }

        try
        {
            (int sourceX, int sourceY, Altitude sourceZ, Rotation sourceRot, int sourceOffset) = (
                wallItem.X,
                wallItem.Y,
                wallItem.Z,
                wallItem.Rotation,
                wallItem.WallOffset
            );

            if (Actions.MoveWallItem(wallItem, x, y, z, rot, wallOffset))
            {
                WallItemMoves.Add(
                    new()
                    {
                        ObjectId = wallItem.ObjectId,
                        IsDirectionRight = wallItem.Rotation != Rotation.South,
                        SourceX = sourceX,
                        SourceY = sourceY,
                        SourceOffsetX = sourceOffset,
                        SourceOffsetY = (int)sourceZ,
                        TargetX = wallItem.X,
                        TargetY = wallItem.Y,
                        TargetOffsetX = wallItem.WallOffset,
                        TargetOffsetY = (int)wallItem.Z,
                        AnimationTime =
                            Policy.AnimationMode == WiredAnimationModeType.Instant
                                ? 0
                                : Policy.AnimationTimeMs,
                    }
                );
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Logger.LogWarning(
                ex,
                "Failed to process wired wall-item movement for item {ItemId} to ({X}, {Y}).",
                wallItem.ObjectId,
                x,
                y
            );
        }

        return Task.CompletedTask;
    }

    public async Task ProcessUserMovementAsync(
        IRoomAvatar avatar,
        int tileIdx,
        SlideAvatarMoveType moveType,
        WiredWalkMode walkMode = WiredWalkMode.KeepIfCloser
    )
    {
        if (avatar is null || !Actions.InBounds(tileIdx))
        {
            return;
        }

        try
        {
            (int sourceX, int sourceY, Altitude sourceZ) = (avatar.X, avatar.Y, avatar.Z);
            Altitude targetZ = Actions.TileHeight(tileIdx);

            // Capture the walk before clearing it: "keep walking" means resuming towards the same goal
            // from wherever the user lands, not replaying a path that was routed from the tile they are
            // being moved off.
            bool wasWalking = avatar.IsWalking;
            int goalTileId = avatar.GoalTileId;
            int sourceTileIdx = Room.ToIdx(sourceX, sourceY);

            // The queued path is stale the moment the avatar is relocated — the walk tick steps through
            // TilePath, so leaving it in place walks the user straight back towards where they started.
            Actions.CancelWalk(avatar);

            if (Actions.RollAvatar(avatar, tileIdx, targetZ))
            {
                // Face the direction moved (unless it was an in-place move).
                Rotation facing =
                    sourceX != avatar.X || sourceY != avatar.Y
                        ? RotationExtensions.FromPoints(sourceX, sourceY, avatar.X, avatar.Y)
                        : avatar.Rotation;

                avatar.SetBodyRotation(facing);
                avatar.SetHeadRotation(facing);

                UserMoves.Add(
                    new()
                    {
                        ObjectId = avatar.ObjectId,
                        SourceX = sourceX,
                        SourceY = sourceY,
                        SourceZ = sourceZ,
                        TargetX = avatar.X,
                        TargetY = avatar.Y,
                        TargetZ = avatar.Z,
                        MoveType = moveType,
                        AnimationTime =
                            moveType == SlideAvatarMoveType.Slide ? Policy.AnimationTimeMs : 0,
                        BodyDirection = facing,
                        HeadDirection = facing,
                    }
                );

                await ResumeWalkAfterMoveAsync(
                    avatar,
                    walkMode,
                    wasWalking,
                    goalTileId,
                    sourceTileIdx,
                    tileIdx
                );
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Logger.LogWarning(
                ex,
                "Failed to process wired user movement for avatar {ObjectId} to tile {TileIdx}.",
                avatar.ObjectId,
                tileIdx
            );
        }
    }

    /// <summary>
    /// Applies the effect's walk mode after the user has been relocated: re-routes them towards the goal
    /// they were heading for (from where they landed), or leaves them stopped. Only ever resumes a walk
    /// that was actually in progress and still has a reachable goal.
    /// </summary>
    private async Task ResumeWalkAfterMoveAsync(
        IRoomAvatar avatar,
        WiredWalkMode walkMode,
        bool wasWalking,
        int goalTileId,
        int sourceTileIdx,
        int targetTileIdx
    )
    {
        if (
            walkMode == WiredWalkMode.Stop
            || !wasWalking
            || goalTileId < 0
            || goalTileId == targetTileIdx
        )
        {
            return;
        }

        if (
            walkMode == WiredWalkMode.KeepIfCloser
            && StepsBetween(targetTileIdx, goalTileId) >= StepsBetween(sourceTileIdx, goalTileId)
        )
        {
            return;
        }

        (int goalX, int goalY) = Actions.GetTileXY(goalTileId);

        await Actions.WalkAvatarToAsync(
            avatar,
            goalX,
            goalY,
            System.Threading.CancellationToken.None
        );
    }

    /// <summary>Steps between two tiles. Avatars move in eight directions, so a diagonal costs the same
    /// as a straight step and the step count is the Chebyshev distance.</summary>
    private int StepsBetween(int fromTileIdx, int toTileIdx)
    {
        (int fromX, int fromY) = Actions.GetTileXY(fromTileIdx);
        (int toX, int toY) = Actions.GetTileXY(toTileIdx);

        return Math.Max(Math.Abs(toX - fromX), Math.Abs(toY - fromY));
    }

    public Task ProcessUserDirectionAsync(
        IRoomAvatar avatar,
        Rotation bodyRotation,
        Rotation headRotation
    )
    {
        if (avatar is null)
        {
            return Task.CompletedTask;
        }

        avatar.SetBodyRotation(bodyRotation);
        avatar.SetHeadRotation(headRotation);

        UserDirections.Add(
            new()
            {
                ObjectId = avatar.ObjectId,
                BodyRotation = avatar.Rotation,
                HeadRotation = avatar.HeadRotation,
            }
        );

        return Task.CompletedTask;
    }

    public ActionContext AsActionContext() => ActionContext.CreateForWired(Room.RoomId);

    public Task SendComposerToRoomAsync(IComposer composer) =>
        Actions.SendComposerToRoomAsync(composer);

    /// <summary>
    /// Makes the named bot speak. Named rather than addressed by id because that is how the client's
    /// setup form asks for it — the builder types a name into the box — and an unknown name is a
    /// stack pointing at a bot that has been picked up, which is ordinary rather than an error.
    /// </summary>
    public Task<bool> ProcessBotChatAsync(
        string botName,
        string text,
        WiredBotChatType chatType,
        PlayerId? whisperTo
    ) =>
        WithBotAsync(
            botName,
            "speak",
            bot => Actions.BotSayAsync(bot.BotId, text, chatType, whisperTo, CancellationToken.None)
        );

    public Task<bool> ProcessBotMovementAsync(string botName, int tileIdx, bool instant)
    {
        if (!Actions.InBounds(tileIdx))
        {
            return Task.FromResult(false);
        }

        (int x, int y) = Actions.GetTileXY(tileIdx);

        return WithBotAsync(
            botName,
            "move",
            bot =>
                instant
                    ? Actions.BotTeleportAsync(bot.BotId, x, y, CancellationToken.None)
                    : Actions.BotWalkToAsync(bot.BotId, x, y, CancellationToken.None)
        );
    }

    public Task<bool> ProcessBotFollowAsync(string botName, PlayerId? target) =>
        WithBotAsync(
            botName,
            "follow",
            bot => Actions.BotSetFollowTargetAsync(bot.BotId, target, CancellationToken.None)
        );

    public Task<bool> ProcessBotFigureAsync(string botName, string figure) =>
        WithBotAsync(
            botName,
            "change look",
            bot => Actions.BotSetFigureAsync(bot.BotId, figure, CancellationToken.None)
        );

    public Task<bool> ProcessBotWalkToPlayerAsync(string botName, PlayerId target)
    {
        if (!Actions.TryGetAvatar(target, out IRoomAvatar? avatar))
        {
            return Task.FromResult(false);
        }

        return WithBotAsync(
            botName,
            "walk over",
            bot => Actions.BotWalkToAsync(bot.BotId, avatar.X, avatar.Y, CancellationToken.None)
        );
    }

    public Task<bool> ProcessGiveHandItemAsync(PlayerId playerId, int handItemId) =>
        Task.FromResult(Actions.GiveHandItem(playerId, handItemId));

    /// <summary>
    /// Runs something against the bot going by a name. A stack naming a bot that is not here is
    /// ordinary rather than exceptional — somebody picked it up — so it answers false and says
    /// nothing.
    /// </summary>
    private async Task<bool> WithBotAsync(string botName, string what, Func<BotSnapshot, Task> act)
    {
        try
        {
            BotSnapshot? bot = await Actions
                .FindBotByNameAsync(botName, CancellationToken.None)
                .ConfigureAwait(true);

            if (bot is null)
            {
                return false;
            }

            await act(bot).ConfigureAwait(true);

            return true;
        }
        catch (Exception ex)
        {
            Diagnostics.Logger.LogWarning(
                ex,
                "Failed to make bot {BotName} {What} in room {RoomId}.",
                botName,
                what,
                Room.RoomId
            );

            return false;
        }
    }
}
