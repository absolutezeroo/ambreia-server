using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using Vortex.Database.Context;
using Vortex.Events.Registry;
using Vortex.Primitives.Action;
using Vortex.Primitives.Events;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Orleans.Snapshots.Room;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Snapshots.Avatars;
using Vortex.Primitives.Rooms.Snapshots.Furniture;
using Vortex.Primitives.Rooms.Snapshots.Mapping;
using Vortex.Protocol.Messages.Outgoing.Room.Action;
using Vortex.Protocol.Messages.Outgoing.Room.Engine;
using Vortex.Protocol.Messages.Outgoing.Room.Layout;
using Vortex.Protocol.Messages.Outgoing.Room.Permissions;
using Vortex.Protocol.Messages.Outgoing.Room.Session;
using Vortex.Rooms.Configuration;

namespace Vortex.Rooms;

internal sealed partial class RoomService(
    ILogger<IRoomService> logger,
    IOptions<RoomConfig> roomConfig,
    IGrainFactory grainFactory,
    ICancellableEventPublisher cancellableEvents
) : IRoomService
{
    private readonly IGrainFactory _grainFactory = grainFactory;
    private readonly ILogger<IRoomService> _logger = logger;
    private readonly RoomConfig _roomConfig = roomConfig.Value;
    private readonly ICancellableEventPublisher _cancellableEvents = cancellableEvents;

    public async Task OpenRoomForPlayerIdAsync(
        ActionContext ctx,
        PlayerId playerId,
        RoomId roomId,
        CancellationToken ct,
        string password = ""
    )
    {
        // A session not yet signed in carries no player: it must not activate a presence grain,
        // nor a room of its choosing.
        if (playerId <= 0)
        {
            return;
        }

        IPlayerPresenceGrain playerPresence = _grainFactory.GetPlayerPresenceGrain(playerId);
        RoomPendingSnapshot pendingRoom = await playerPresence
            .GetPendingRoomAsync()
            .ConfigureAwait(false);

        // Only an entry still in flight is a duplicate worth dropping. Once the player is in, the
        // same room id means a reload -- the client asks for the room you are standing in as
        // readily as for any other -- and by the time this arrives RoomSessionManager.createSession
        // has already disposed the session and purged the room content. Returning early there left
        // the client on a black screen with nothing left to ask for.
        if (pendingRoom.RoomId == roomId && !pendingRoom.Approved)
        {
            return;
        }

        // A dungeon is played in the group's own copy (plan D21): the client is sent there, and
        // asks for it in turn.
        RoomId? target = await _grainFactory
            .GetRoomZone(roomId)
            .InstanceForAsync(playerId)
            .ConfigureAwait(false);

        if (target is null)
        {
            await playerPresence
                .SendComposerAsync(
                    new CantConnectMessageComposer { ErrorType = RoomConnectionErrorType.RoomFull }
                )
                .ConfigureAwait(false);

            return;
        }

        if (target.Value != roomId)
        {
            await playerPresence
                .SendComposerAsync(new RoomForwardMessageComposer { RoomId = target.Value })
                .ConfigureAwait(false);

            return;
        }

        // Room entry is the one flow that legitimately spans the room: core activation, the
        // controller level and the avatar list take part. This is what the
        // IRoomGrain aggregate is for -- narrowing it here would mean four references, not less
        // coupling.
        IRoomGrain room = _grainFactory.GetRoomGrain(roomId);

        // Back within the grace period (spec Personnage §10), into the room still holding the
        // avatar: it is kept, and SetActiveRoomAsync sees the same room and makes no second one.
        bool resumed = await ResumeOrLeaveAsync(
                playerPresence,
                roomId,
                async () =>
                    (await room.GetAllAvatarSnapshotsAsync(ct).ConfigureAwait(false)).Any(a =>
                        a is RoomPlayerAvatarSnapshot && a.WebId == playerId.Value
                    ),
                ct
            )
            .ConfigureAwait(false);

        // Pending, not approved: the gates below have not run yet. Approval is granted in
        // CompleteRoomEntryAsync, the one place every admission passes through.
        await playerPresence.SetPendingRoomAsync(roomId, false).ConfigureAwait(false);

        await playerPresence
            .SendComposerAsync(new OpenConnectionMessageComposer { RoomId = roomId })
            .ConfigureAwait(false);

        await room.EnsureRoomActiveAsync(ct).ConfigureAwait(false);

        RoomSnapshot snapshot = await room.GetSnapshotAsync().ConfigureAwait(false);
        RoomControllerType controllerLevel = await room.GetControllerLevelAsync(ctx, ct)
            .ConfigureAwait(false);

        // A resumed player was admitted before the cut; their own waiting avatar must not count
        // them out of a full room, nor a password they cannot resend lock them out.
        if (!resumed && controllerLevel < RoomControllerType.Rights)
        {
            ImmutableArray<RoomAvatarSnapshot> currentAvatars =
                await room.GetAllAvatarSnapshotsAsync(ct).ConfigureAwait(false);
            bool isFull = snapshot.PlayersMax > 0 && currentAvatars.Length >= snapshot.PlayersMax;
            // A locked door has no doorbell any more (the cut of 2026-10-07): it simply refuses.
            bool isRefused =
                snapshot.DoorMode == RoomDoorModeType.Locked
                || (
                    snapshot.DoorMode == RoomDoorModeType.Password
                    && !string.Equals(snapshot.Password, password, StringComparison.Ordinal)
                );

            if (isFull || isRefused)
            {
                await playerPresence
                    .SendComposerAsync(
                        new CantConnectMessageComposer
                        {
                            ErrorType = isFull
                                ? RoomConnectionErrorType.RoomFull
                                : RoomConnectionErrorType.NoEntry,
                        }
                    )
                    .ConfigureAwait(false);
                await playerPresence
                    .SetPendingRoomAsync(RoomId.Invalid, false)
                    .ConfigureAwait(false);
                return;
            }
        }

        await CompleteRoomEntryAsync(ctx, playerId, roomId, ct).ConfigureAwait(false);
    }

    /// <summary>True when the player comes back, within the grace period, to the room that still
    /// holds their avatar; otherwise the room they were in is left, as any entry does.</summary>
    internal static async Task<bool> ResumeOrLeaveAsync(
        IPlayerPresenceGrain presence,
        RoomId roomId,
        Func<Task<bool>> roomHoldsTheAvatar,
        CancellationToken ct
    )
    {
        if (
            await presence.TakeResumeAsync(roomId).ConfigureAwait(false)
            && await roomHoldsTheAvatar().ConfigureAwait(false)
        )
        {
            return true;
        }

        await presence.ClearActiveRoomAsync(ct).ConfigureAwait(false);

        return false;
    }

    private async Task CompleteRoomEntryAsync(
        ActionContext ctx,
        PlayerId playerId,
        RoomId roomId,
        CancellationToken ct
    )
    {
        IPlayerPresenceGrain playerPresence = _grainFactory.GetPlayerPresenceGrain(playerId);

        // The last gate, and the only one outside code can add. Placed here, the one place every
        // admission passes through. Nothing has been sent to the client yet, so a refusal here
        // looks exactly like a locked door refusing.
        EventContext entering = await _cancellableEvents
            .PublishCancellableAsync(new PlayerEnteringRoomEvent(playerId, roomId.Value), ct)
            .ConfigureAwait(false);

        if (entering.Cancel)
        {
            _logger.LogInformation(
                "Room entry of player {PlayerId} into room {RoomId} was cancelled: {Reason}",
                playerId.Value,
                roomId.Value,
                entering.CancelReason ?? string.Empty
            );

            await playerPresence
                .SendComposerAsync(
                    new CantConnectMessageComposer { ErrorType = RoomConnectionErrorType.NoEntry }
                )
                .ConfigureAwait(false);
            await playerPresence.SetPendingRoomAsync(RoomId.Invalid, false).ConfigureAwait(false);

            // A resumed avatar refused here would stand in the room for nobody; otherwise the room
            // was already left and this does nothing.
            await playerPresence.ClearActiveRoomAsync(ct).ConfigureAwait(false);

            return;
        }

        // Builds the whole entry payload -- core, security, avatars, map and furni. Deliberately
        // the aggregate; see OpenRoomForPlayerIdAsync above.
        IRoomGrain room = _grainFactory.GetRoomGrain(roomId);

        RoomSnapshot snapshot = await room.GetSnapshotAsync().ConfigureAwait(false);
        RoomControllerType controllerLevel = await room.GetControllerLevelAsync(ctx, ct)
            .ConfigureAwait(false);
        // Moderator outranks Owner in this ladder, so an equality test excluded staff from the
        // owner-facing entry data while PlayerPresenceGrain already sent them YouAreOwner. The two
        // sites contradicted each other; both are >= now.
        bool isOwner = controllerLevel >= RoomControllerType.Owner;
        ImmutableArray<RoomAvatarSnapshot> avatarSnapshots = await room.GetAllAvatarSnapshotsAsync(
                ct
            )
            .ConfigureAwait(false);

        RoomMapSnapshot mapSnapshot = await room.GetMapSnapshotAsync(ct).ConfigureAwait(false);

        ImmutableArray<KeyValuePair<string, string>> roomProperties =
            await room.GetRoomPropertiesAsync().ConfigureAwait(false);

        // Order below follows an official room entry, captured packet by packet. It is not
        // cosmetic: the client builds the room as the burst arrives, and every message here lands
        // on a session that already exists rather than being queued against one. Reads are gathered
        // before the sends so the sequence reads as the sequence, with no grain round trip wedged
        // between two packets that Sulake sends back to back.

        await playerPresence
            .SendComposerAsync(
                new RoomReadyMessageComposer { WorldType = snapshot.WorldType, RoomId = roomId },
                new RoomEntryTileMessageComposer
                {
                    X = mapSnapshot.DoorX,
                    Y = mapSnapshot.DoorY,
                    Rotation = mapSnapshot.DoorRotation,
                }
            )
            .ConfigureAwait(false);

        // Wallpaper, floor and landscape, before the maps rather than after the visualisation
        // settings. This is where the official server puts them, and it is the order in which the
        // client has the paint in hand before it is handed the geometry to paint.
        if (roomProperties.Length > 0)
        {
            await playerPresence
                .SendComposerAsync([
                    .. roomProperties.Select(
                        IComposer (x) =>
                            new RoomPropertyMessageComposer { Key = x.Key, Value = x.Value }
                    ),
                ])
                .ConfigureAwait(false);
        }

        await playerPresence
            .SendComposerAsync(
                new HeightMapMessageComposer
                {
                    Width = mapSnapshot.Width,
                    Size = mapSnapshot.Size,
                    Heights = mapSnapshot.TileEncodedHeights,
                },
                new FloorHeightMapMessageComposer
                {
                    ScaleType = _roomConfig.DefaultRoomScale,
                    // The room's own, from its row. This was the config's 0 for every room --
                    // fixed walls at the lowest height -- so a stored height was read back nowhere.
                    FixedWallsHeight = snapshot.WallHeight,
                    ModelData = mapSnapshot.ModelData,
                    AreaHideData = [],
                    // Where the client parks the camera before the first avatar exists
                    // (room/_SafeCls_1984.as:640). Sulake's own value is unknown; the door tile is
                    // the one place in the model that is always walkable and always where the
                    // player is about to appear, and it beats the (0,0) corner we used to send.
                    CameraInitX = mapSnapshot.DoorX,
                    CameraInitY = mapSnapshot.DoorY,
                    CameraInitZ = 0f,
                },
                new RoomVisualizationSettingsMessageComposer
                {
                    WallsHidden = snapshot.HideWalls,
                    WallThickness = snapshot.WallThickness,
                    FloorThickness = snapshot.FloorThickness,
                }
            )
            .ConfigureAwait(false);

        ImmutableDictionary<PlayerId, string> ownersSnapshot = await room.GetAllOwnersAsync(ct)
            .ConfigureAwait(false);
        ImmutableArray<RoomFloorItemSnapshot> floorSnapshot =
            await room.GetAllFloorItemSnapshotsAsync(ct).ConfigureAwait(false);
        ImmutableArray<RoomWallItemSnapshot> wallSnapshot = await room.GetAllWallItemSnapshotsAsync(
                ct
            )
            .ConfigureAwait(false);

        // Neither a worn effect nor a hand item is part of the Users payload, so each one has to be
        // replayed for the player walking in.
        IComposer[] replayComposers =
        [
            .. avatarSnapshots
                .OfType<RoomPlayerAvatarSnapshot>()
                .Where(x => x.CurrentEffectId != 0)
                .Select(
                    IComposer (x) =>
                        new AvatarEffectMessageComposer
                        {
                            UserId = x.ObjectId,
                            EffectId = x.CurrentEffectId,
                            DelayMilliseconds = 0,
                        }
                ),
            .. avatarSnapshots
                .OfType<RoomPlayerAvatarSnapshot>()
                .Where(x => x.CarryItemId != 0)
                .Select(
                    IComposer (x) =>
                        new CarryObjectMessageComposer
                        {
                            UserId = x.ObjectId.Value,
                            ItemType = x.CarryItemId,
                        }
                ),
        ];

        // Who is here, then what is here, then whose room it is -- Sulake's order. Neither list
        // carries the arriving player: their avatar is created by SetActiveRoomAsync at the end of
        // this method and broadcast to the whole room, which is the second Users an official capture
        // shows. The first one is the occupants who were already standing here, and it is empty in
        // that capture only because the room was.
        await playerPresence
            .SendComposerAsync(
                new UsersMessageComposer { Avatars = avatarSnapshots },
                new UserUpdateMessageComposer { Avatars = avatarSnapshots },
                new ObjectsMessageComposer
                {
                    OwnerNames = ownersSnapshot,
                    FloorItems = floorSnapshot,
                },
                new ItemsMessageComposer { OwnerNames = ownersSnapshot, WallItems = wallSnapshot },
                new RoomEntryInfoMessageComposer { RoomId = roomId, IsOwner = isOwner }
            )
            .ConfigureAwait(false);

        await playerPresence
            .SendComposerAsync(
                // Sets chooserDisabled, freeFurniMovementsEnabled and invisibleFurni on the client's
                // room object, per room id. We write only the first boolean -- the client's parser
                // guards each of the other two on bytes remaining -- and none of the three is a room
                // setting here, so this pins them to their defaults rather than leaving the client's
                // own defaults to decide. The official server sends it on every entry.
                new HanditemConfigurationMessageComposer { IsHanditemControlBlocked = false },
                new YouAreControllerMessageComposer
                {
                    RoomId = roomId,
                    ControllerLevel = controllerLevel,
                }
            )
            .ConfigureAwait(false);

        if (isOwner)
        {
            await playerPresence
                .SendComposerAsync(new YouAreOwnerMessageComposer { RoomId = roomId })
                .ConfigureAwait(false);
        }

        if (replayComposers.Length > 0)
        {
            await playerPresence.SendComposerAsync(replayComposers).ConfigureAwait(false);
        }

        await playerPresence.SetActiveRoomAsync(roomId, ct).ConfigureAwait(false);

        // Approved only once the player is genuinely in. Every gate above answered, the payload is
        // sent and the avatar exists, so the flag means exactly one thing: this player was admitted
        // here. Granting it any earlier approves a request a gate above may still refuse.
        await playerPresence.SetPendingRoomAsync(roomId, true).ConfigureAwait(false);
    }

    public async Task CloseRoomForPlayerAsync(PlayerId playerId, CancellationToken ct)
    {
        if (playerId <= 0)
        {
            return;
        }

        IPlayerPresenceGrain playerPresence = _grainFactory.GetPlayerPresenceGrain(playerId);
        RoomPendingSnapshot pendingRoom = await playerPresence
            .GetPendingRoomAsync()
            .ConfigureAwait(false);

        if (pendingRoom.RoomId > 0)
        {
            await playerPresence.SetPendingRoomAsync(RoomId.Invalid, false).ConfigureAwait(false);
        }

        await playerPresence.ClearActiveRoomAsync(ct).ConfigureAwait(false);

        await playerPresence
            .SendComposerAsync(new CloseConnectionMessageComposer())
            .ConfigureAwait(false);
    }

    public async Task ClickTileAsync(
        ActionContext ctx,
        int targetX,
        int targetY,
        int? targetZKey,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0 || ctx.RoomId <= 0)
        {
            return;
        }

        await _grainFactory
            .GetRoomMap(ctx.RoomId)
            .ClickTileAsync(ctx, targetX, targetY, ct)
            .ConfigureAwait(false);
        await _grainFactory
            .GetRoomAvatars(ctx.RoomId)
            .WalkAvatarToAsync(ctx, targetX, targetY, targetZKey, ct)
            .ConfigureAwait(false);
    }
}
