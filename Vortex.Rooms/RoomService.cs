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
using Vortex.Primitives.Rooms.RaidProtection;
using Vortex.Primitives.Rooms.Snapshots.Avatars;
using Vortex.Primitives.Rooms.Snapshots.Furniture;
using Vortex.Primitives.Rooms.Snapshots.Mapping;
using Vortex.Primitives.Rooms.Snapshots.Wired;
using Vortex.Protocol.Messages.Outgoing.Navigator;
using Vortex.Protocol.Messages.Outgoing.Room.Action;
using Vortex.Protocol.Messages.Outgoing.Room.Engine;
using Vortex.Protocol.Messages.Outgoing.Room.Layout;
using Vortex.Protocol.Messages.Outgoing.Room.Permissions;
using Vortex.Protocol.Messages.Outgoing.Room.RaidProtection;
using Vortex.Protocol.Messages.Outgoing.Room.Session;
using Vortex.Protocol.Messages.Outgoing.Userdefinedroomevents;
using Vortex.Protocol.Messages.Outgoing.Userdefinedroomevents.Wiredmenu;
using Vortex.Rooms.Configuration;

namespace Vortex.Rooms;

internal sealed partial class RoomService(
    ILogger<IRoomService> logger,
    IOptions<RoomConfig> roomConfig,
    ISessionGateway sessionGateway,
    IGrainFactory grainFactory,
    IDbContextFactory<VortexDbContext> dbContextFactory,
    IRoomModerationStore roomModerationStore,
    IRoomAdvertisementService roomAdvertisements,
    IEventPublisher events,
    ICancellableEventPublisher cancellableEvents
) : IRoomService
{
    private readonly IRoomAdvertisementService _roomAdvertisements = roomAdvertisements;
    private readonly IDbContextFactory<VortexDbContext> _dbContextFactory = dbContextFactory;
    private readonly IGrainFactory _grainFactory = grainFactory;
    private readonly ILogger<IRoomService> _logger = logger;
    private readonly RoomConfig _roomConfig = roomConfig.Value;
    private readonly IRoomModerationStore _roomModerationStore = roomModerationStore;
    private readonly ISessionGateway _sessionGateway = sessionGateway;
    private readonly IEventPublisher _events = events;
    private readonly ICancellableEventPublisher _cancellableEvents = cancellableEvents;

    public async Task OpenRoomForPlayerIdAsync(
        ActionContext ctx,
        PlayerId playerId,
        RoomId roomId,
        CancellationToken ct,
        string password = ""
    )
    {
        IPlayerPresenceGrain playerPresence = _grainFactory.GetPlayerPresenceGrain(playerId);
        RoomPendingSnapshot pendingRoom = await playerPresence
            .GetPendingRoomAsync()
            .ConfigureAwait(false);

        if (
            await _roomModerationStore
                .IsBannedAsync(roomId.Value, playerId.Value, ct)
                .ConfigureAwait(false)
        )
        {
            await playerPresence
                .SendComposerAsync(
                    new CantConnectMessageComposer { ErrorType = RoomConnectionErrorType.Banned }
                )
                .ConfigureAwait(false);
            return;
        }

        // Only an entry still in flight is a duplicate worth dropping. Once the player is in, the
        // same room id means a reload -- the navigator forwards to the room you are standing in as
        // readily as to any other -- and by the time this arrives RoomSessionManager.createSession
        // has already disposed the session and purged the room content. Returning early there left
        // the client on a black screen with nothing left to ask for.
        if (pendingRoom.RoomId == roomId && !pendingRoom.Approved)
        {
            return;
        }

        await playerPresence.ClearActiveRoomAsync(ct).ConfigureAwait(false);

        // Pending, not approved: the gates below have not run yet. This used to claim approval here
        // and never take it back on the doorbell path, where the request legitimately stays pending
        // while the room is asked -- so a ringer who asked for the entry payload without waiting
        // for an answer was handed it and walked in. Approval is granted in CompleteRoomEntryAsync,
        // the one place every admission passes through.
        await playerPresence.SetPendingRoomAsync(roomId, false).ConfigureAwait(false);

        // Room entry is the one flow that legitimately spans the room: core activation, the
        // security gate, the avatar list and the doorbell all take part. This is what the
        // IRoomGrain aggregate is for -- narrowing it here would mean four references, not less
        // coupling.
        IRoomGrain room = _grainFactory.GetRoomGrain(roomId);

        await playerPresence
            .SendComposerAsync(new OpenConnectionMessageComposer { RoomId = roomId })
            .ConfigureAwait(false);

        await room.EnsureRoomActiveAsync(ct).ConfigureAwait(false);

        RoomSnapshot snapshot = await room.GetSnapshotAsync().ConfigureAwait(false);
        RoomControllerType controllerLevel = await room.GetControllerLevelAsync(ctx, ct)
            .ConfigureAwait(false);

        if (controllerLevel < RoomControllerType.Rights)
        {
            ImmutableArray<RoomAvatarSnapshot> currentAvatars =
                await room.GetAllAvatarSnapshotsAsync(ct).ConfigureAwait(false);
            bool isFull = snapshot.PlayersMax > 0 && currentAvatars.Length >= snapshot.PlayersMax;
            bool isPasswordMismatch =
                snapshot.DoorMode == RoomDoorModeType.Password
                && !string.Equals(snapshot.Password, password, StringComparison.Ordinal);

            if (isFull || isPasswordMismatch)
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

            if (snapshot.DoorMode == RoomDoorModeType.Locked)
            {
                // Locked doors ring the doorbell instead of a flat reject: the requester waits
                // (RoomPendingSnapshot stays set) while every present owner/rights-holder is
                // notified. Resolution arrives via AnswerDoorbellAsync (LetUserInMessageHandler)
                // or the room tick's timeout sweep (RoomGrain.Doorbell.cs) if nobody answers.
                await room.RegisterDoorbellRingAsync(playerId, ct).ConfigureAwait(false);
                return;
            }
        }

        await CompleteRoomEntryAsync(ctx, playerId, roomId, ct).ConfigureAwait(false);
    }

    private async Task CompleteRoomEntryAsync(
        ActionContext ctx,
        PlayerId playerId,
        RoomId roomId,
        CancellationToken ct
    )
    {
        IPlayerPresenceGrain playerPresence = _grainFactory.GetPlayerPresenceGrain(playerId);

        // The last gate, and the only one outside code can add. Placed here rather than in
        // OpenRoomForPlayerIdAsync so it also covers the doorbell path: a player let in by an owner
        // arrives through this method and nowhere else. Nothing has been sent to the client yet, so
        // a refusal here looks exactly like a locked door refusing.
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

            return;
        }

        // Builds the whole entry payload -- core, security, avatars, map and furni. Deliberately
        // the aggregate; see EnterRoomAsync above.
        IRoomGrain room = _grainFactory.GetRoomGrain(roomId);

        // Raid protection judges the arrival here and counts it, which is why it sits in this
        // method and not in OpenRoomForPlayerIdAsync: this is the one place every entry passes
        // through, doorbell admissions included, and it is reached exactly once per entry. Earlier
        // would count arrivals that a full room, a wrong password or a cancelled entry then turned
        // away, and drag the threshold down onto honest traffic.
        RaidEntryDecision raid = await room.EvaluateEntryAsync(playerId, ct).ConfigureAwait(false);

        if (raid.Verdict != RaidEntryVerdict.Allow)
        {
            _logger.LogInformation(
                "Room {RoomId} turned player {PlayerId} away: raid protection returned {Verdict}.",
                roomId.Value,
                playerId.Value,
                raid.Verdict
            );

            await playerPresence
                .SendComposerAsync(
                    new CantConnectMessageComposer
                    {
                        ErrorType =
                            raid.Verdict == RaidEntryVerdict.Ban
                                ? RoomConnectionErrorType.Banned
                                : RoomConnectionErrorType.NoEntry,
                    }
                )
                .ConfigureAwait(false);
            await playerPresence.SetPendingRoomAsync(RoomId.Invalid, false).ConfigureAwait(false);

            return;
        }

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
        bool canRate = await room.CanRateAsync(playerId, ct).ConfigureAwait(false);

        ImmutableArray<KeyValuePair<string, string>> roomProperties =
            await room.GetRoomPropertiesAsync().ConfigureAwait(false);

        // Order below follows an official room entry, captured packet by packet. It is not
        // cosmetic: the client builds the room as the burst arrives, and every message here lands
        // on a session that already exists rather than being queued against one. Reads are gathered
        // before the sends so the sequence reads as the sequence, with no grain round trip wedged
        // between two packets that Sulake sends back to back.

        // The chat-style selector is rebuilt from scratch when the room's chat input is created, off
        // whatever the session sets hold at that instant. The client asks for them once at session
        // init, so a room entered before that answer lands -- the home room, every time -- builds
        // the picker without a single unlockable bubble in it. The official server sends them here,
        // immediately after OpenConnection, which is as early as our gates allow: a refused entry
        // has returned well above this line.
        await _grainFactory
            .GetPlayerGrain(playerId)
            .PushOwnedChatStylesAsync(ct)
            .ConfigureAwait(false);

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
                    FixedWallsHeight = _roomConfig.DefaultWallHeight,
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
        bool hasRights = controllerLevel >= RoomControllerType.Rights;

        // Until this is sent the client assumes no click-user box exists: it opens the context menu
        // itself and never sends WiredClickUser. An official capture of a room entry shows the
        // server pushing this as part of the entry burst -- after OpenConnection the client asks
        // for nothing at all, so there is no later request to hang it on.
        WiredClickUserSnapshot clickUser = await room.GetClickUserStateAsync(ct)
            .ConfigureAwait(false);

        // None of a dance, a worn effect or a hand item is part of the Users payload, so each one
        // has to be replayed for the player walking in. Bots dance too, and their dance is as
        // absent from that payload as a player's.
        IComposer[] replayComposers =
        [
            .. avatarSnapshots
                .OfType<RoomPlayerAvatarSnapshot>()
                .Where(x => x.DanceType != AvatarDanceType.None)
                .Select(
                    IComposer (x) =>
                        new DanceMessageComposer { ObjectId = x.ObjectId, DanceType = x.DanceType }
                ),
            .. avatarSnapshots
                .OfType<RoomBotAvatarSnapshot>()
                .Where(x => x.DanceType != AvatarDanceType.None)
                .Select(
                    IComposer (x) =>
                        new DanceMessageComposer { ObjectId = x.ObjectId, DanceType = x.DanceType }
                ),
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

        // The room's advertisement, if it is being advertised right now. There is no "no event"
        // form to send: this client assigns whatever arrives to roomEventData without testing it,
        // so an empty one would leave it holding a blank advertisement rather than none. Absence is
        // how the client is told there is nothing, and RoomEventCancel is how a live one is cleared.
        RoomAdvertisementSnapshot? advertisement = await _roomAdvertisements
            .GetActiveAdvertisementAsync(roomId.Value, ct)
            .ConfigureAwait(false);

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

        if (advertisement is not null)
        {
            DateTime now = DateTime.UtcNow;

            await playerPresence
                .SendComposerAsync(
                    new RoomEventMessageComposer
                    {
                        AdvertisementId = advertisement.AdvertisementId,
                        OwnerPlayerId = advertisement.OwnerPlayerId,
                        OwnerName = advertisement.OwnerName,
                        RoomId = advertisement.RoomId,
                        Name = advertisement.Name,
                        Description = advertisement.Description,
                        MinutesSinceCreation = Math.Max(
                            0,
                            (int)(now - advertisement.CreatedAt).TotalMinutes
                        ),
                        MinutesRemaining = Math.Max(
                            0,
                            (int)(advertisement.ExpiresAt - now).TotalMinutes
                        ),
                        CategoryId = advertisement.CategoryId,
                    }
                )
                .ConfigureAwait(false);
        }

        await playerPresence
            .SendComposerAsync(
                // Sets chooserDisabled, freeFurniMovementsEnabled and invisibleFurni on the client's
                // room object, per room id. We write only the first boolean -- the client's parser
                // guards each of the other two on bytes remaining -- and none of the three is a room
                // setting here, so this pins them to their defaults rather than leaving the client's
                // own defaults to decide. The official server sends it on every entry.
                new HanditemConfigurationMessageComposer { IsHanditemControlBlocked = false },
                new WiredEnvironmentMessageComposer { HasClickUserWired = clickUser.Present },
                new YouAreControllerMessageComposer
                {
                    RoomId = roomId,
                    ControllerLevel = controllerLevel,
                },
                new WiredPermissionsEventMessageComposer
                {
                    CanModify = hasRights,
                    CanRead = hasRights,
                }
            )
            .ConfigureAwait(false);

        if (isOwner)
        {
            await playerPresence
                .SendComposerAsync(new YouAreOwnerMessageComposer { RoomId = roomId })
                .ConfigureAwait(false);
        }

        // After the rights, not before the map. The score rides along on the snapshot already in
        // hand; only "may this player still vote" costs a lookup. The client shows the like button
        // off canRate alone and never withdraws it by itself, so a blanket false hid the button in
        // every room and a blanket true offered a vote the grain would refuse.
        await playerPresence
            .SendComposerAsync(
                new RoomRatingMessageComposer { Rating = snapshot.Score, CanRate = canRate }
            )
            .ConfigureAwait(false);

        if (replayComposers.Length > 0)
        {
            await playerPresence.SendComposerAsync(replayComposers).ConfigureAwait(false);
        }

        // Last, and to everyone -- including the visitors who get a plain "no". The official client
        // gates the whole raid-protection feature on this one flag, answers to its own messages
        // included, so a player who never receives it has it silently switched off. It goes out at
        // the end of entry because the client drops it unless the room session is already live.
        await playerPresence
            .SendComposerAsync(
                new RaidProtectionCapabilityMessageComposer
                {
                    RoomId = roomId.Value,
                    CanManage = raid.CanManage,
                }
            )
            .ConfigureAwait(false);

        await playerPresence.SetActiveRoomAsync(roomId, ct).ConfigureAwait(false);

        // Approved only once the player is genuinely in. Every gate above answered, the payload is
        // sent and the avatar exists, so the flag means exactly one thing: this player was admitted
        // here. Granting it any earlier reopens the hole it closes -- a request stays pending for as
        // long as a locked room's doorbell rings.
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
            // Covers cancelling a doorbell ring: the requester never became Active while
            // waiting, so ClearActiveRoomAsync below is a no-op for that case.
            await _grainFactory
                .GetRoomDoorbell(pendingRoom.RoomId)
                .TryRemoveDoorbellRingAsync(playerId, ct)
                .ConfigureAwait(false);
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

    public async Task PickupItemInRoomAsync(
        ActionContext ctx,
        RoomObjectId itemId,
        CancellationToken ct,
        bool isConfirm = true
    )
    {
        if (ctx.PlayerId <= 0 || ctx.RoomId <= 0 || itemId <= 0)
        {
            return;
        }

        IRoomFurni roomGrain = _grainFactory.GetRoomFurni(ctx.RoomId);

        await roomGrain.RemoveItemByIdAsync(ctx, itemId, ct).ConfigureAwait(false);
    }

    public async Task UseItemInRoomAsync(
        ActionContext ctx,
        RoomObjectId itemId,
        CancellationToken ct,
        int param = -1
    )
    {
        if (ctx.PlayerId <= 0 || ctx.RoomId <= 0 || itemId <= 0)
        {
            return;
        }

        IRoomFurni roomGrain = _grainFactory.GetRoomFurni(ctx.RoomId);

        await roomGrain.UseItemByIdAsync(ctx, itemId, ct, param).ConfigureAwait(false);
    }

    public async Task ClickItemInRoomAsync(
        ActionContext ctx,
        RoomObjectId itemId,
        CancellationToken ct,
        int param = -1
    )
    {
        if (ctx.PlayerId <= 0 || ctx.RoomId <= 0 || itemId <= 0)
        {
            return;
        }

        IRoomFurni roomGrain = _grainFactory.GetRoomFurni(ctx.RoomId);

        await roomGrain.ClickItemByIdAsync(ctx, itemId, ct, param).ConfigureAwait(false);
    }
}
