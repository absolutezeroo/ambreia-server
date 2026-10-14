using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vortex.Events.Registry;
using Vortex.Primitives.Action;
using Vortex.Primitives.Events;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Orleans.Snapshots.Room;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Snapshots.Avatars;
using Vortex.Primitives.Rooms.Snapshots.Furniture;
using Vortex.Primitives.Rooms.Snapshots.Mapping;
using Vortex.Protocol.Messages.Outgoing.Room.Action;
using Vortex.Protocol.Messages.Outgoing.Room.Engine;
using Vortex.Protocol.Messages.Outgoing.Room.Layout;
using Vortex.Protocol.Messages.Outgoing.Room.Permissions;
using Vortex.Protocol.Messages.Outgoing.Room.Session;
using Vortex.Protocol.Messages.Outgoing.Rpg.Interaction;

namespace Vortex.Rooms.Grains;

public sealed partial class RoomGrain
{
    public async Task<RoomAdmissionSnapshot> AdmitAsync(
        ActionContext ctx,
        string password,
        bool resumed,
        CancellationToken ct
    )
    {
        await EnsureRoomActiveAsync(ct).ConfigureAwait(true);

        RoomSnapshot snapshot = _state.RoomSnapshot;
        RoomControllerType controllerLevel = await GetControllerLevelAsync(ctx, ct)
            .ConfigureAwait(true);
        ImmutableArray<RoomAvatarSnapshot> avatarSnapshots = await GetAllAvatarSnapshotsAsync(ct)
            .ConfigureAwait(true);

        // A resumed player was admitted before the cut; their own waiting avatar must not count
        // them out of a full room, nor a password they cannot resend lock them out.
        if (!resumed && controllerLevel < RoomControllerType.Rights)
        {
            bool isFull = snapshot.PlayersMax > 0 && avatarSnapshots.Length >= snapshot.PlayersMax;
            // A locked door has no doorbell any more (the cut of 2026-10-07): it simply refuses.
            bool isRefused =
                snapshot.DoorMode == RoomDoorModeType.Locked
                || (
                    snapshot.DoorMode == RoomDoorModeType.Password
                    && !string.Equals(snapshot.Password, password, StringComparison.Ordinal)
                );

            if (isFull || isRefused)
            {
                return new RoomAdmissionSnapshot
                {
                    Refusal = isFull
                        ? RoomConnectionErrorType.RoomFull
                        : RoomConnectionErrorType.NoEntry,
                };
            }
        }

        // The last gate, and the only one outside code can add. Nothing has been sent to the
        // client yet, so a refusal here looks exactly like a locked door refusing.
        EventContext entering = await _cancellableEvents
            .PublishCancellableAsync(
                new PlayerEnteringRoomEvent(ctx.PlayerId, _state.RoomId.Value),
                ct
            )
            .ConfigureAwait(true);

        if (entering.Cancel)
        {
            _logger.LogInformation(
                "Room entry of player {PlayerId} into room {RoomId} was cancelled: {Reason}",
                ctx.PlayerId.Value,
                _state.RoomId.Value,
                entering.CancelReason ?? string.Empty
            );

            return new RoomAdmissionSnapshot { Refusal = RoomConnectionErrorType.NoEntry };
        }

        return new RoomAdmissionSnapshot
        {
            Burst = await BuildEntryBurstAsync(
                    ctx.PlayerId,
                    snapshot,
                    controllerLevel,
                    avatarSnapshots,
                    resumed,
                    ct
                )
                .ConfigureAwait(true),
        };
    }

    /// <summary>
    /// The order below follows an official room entry, captured packet by packet. It is not
    /// cosmetic: the client builds the room as the burst arrives, and every message here lands on
    /// a session that already exists rather than being queued against one.
    /// </summary>
    private async Task<ImmutableArray<IComposer>> BuildEntryBurstAsync(
        PlayerId player,
        RoomSnapshot snapshot,
        RoomControllerType controllerLevel,
        ImmutableArray<RoomAvatarSnapshot> avatarSnapshots,
        bool resumed,
        CancellationToken ct
    )
    {
        // Moderator outranks Owner in this ladder, so an equality test excluded staff from the
        // owner-facing entry data while PlayerPresenceGrain already sent them YouAreOwner. The two
        // sites contradicted each other; both are >= now.
        bool isOwner = controllerLevel >= RoomControllerType.Owner;
        RoomMapSnapshot mapSnapshot = await GetMapSnapshotAsync(ct).ConfigureAwait(true);
        ImmutableDictionary<PlayerId, string> ownersSnapshot = await GetAllOwnersAsync(ct)
            .ConfigureAwait(true);
        ImmutableArray<RoomFloorItemSnapshot> floorSnapshot = await GetAllFloorItemSnapshotsAsync(
                ct
            )
            .ConfigureAwait(true);
        ImmutableArray<RoomWallItemSnapshot> wallSnapshot = await GetAllWallItemSnapshotsAsync(ct)
            .ConfigureAwait(true);

        ImmutableArray<IComposer>.Builder burst = ImmutableArray.CreateBuilder<IComposer>();

        burst.Add(
            new RoomReadyMessageComposer { WorldType = snapshot.WorldType, RoomId = _state.RoomId }
        );
        burst.Add(
            new RoomEntryTileMessageComposer
            {
                X = mapSnapshot.DoorX,
                Y = mapSnapshot.DoorY,
                Rotation = mapSnapshot.DoorRotation,
            }
        );

        // Wallpaper, floor and landscape, before the maps rather than after the visualisation
        // settings. This is where the official server puts them, and it is the order in which the
        // client has the paint in hand before it is handed the geometry to paint.
        foreach (KeyValuePair<string, string> property in _state.RoomProperties)
        {
            burst.Add(
                new RoomPropertyMessageComposer { Key = property.Key, Value = property.Value }
            );
        }

        burst.Add(
            new HeightMapMessageComposer
            {
                Width = mapSnapshot.Width,
                Size = mapSnapshot.Size,
                Heights = mapSnapshot.TileEncodedHeights,
            }
        );
        burst.Add(
            new FloorHeightMapMessageComposer
            {
                ScaleType = _roomConfig.DefaultRoomScale,
                // The room's own, from its row. This was the config's 0 for every room -- fixed
                // walls at the lowest height -- so a stored height was read back nowhere.
                FixedWallsHeight = snapshot.WallHeight,
                ModelData = mapSnapshot.ModelData,
                AreaHideData = [],
                // Where the client parks the camera before the first avatar exists
                // (room/_SafeCls_1984.as:640). Sulake's own value is unknown; the door tile is the
                // one place in the model that is always walkable and always where the player is
                // about to appear, and it beats the (0,0) corner we used to send.
                CameraInitX = mapSnapshot.DoorX,
                CameraInitY = mapSnapshot.DoorY,
                CameraInitZ = 0f,
            }
        );
        burst.Add(
            new RoomVisualizationSettingsMessageComposer
            {
                WallsHidden = snapshot.HideWalls,
                WallThickness = snapshot.WallThickness,
                FloorThickness = snapshot.FloorThickness,
            }
        );

        // Who is here, then what is here, then whose room it is -- Sulake's order. Neither list
        // carries the arriving player: their avatar is created when the presence grain makes this
        // its active room, and broadcast to the whole room, which is the second Users an official
        // capture shows. The first one is the occupants who were already standing here, and it is
        // empty in that capture only because the room was.
        burst.Add(new UsersMessageComposer { Avatars = avatarSnapshots });
        burst.Add(new UserUpdateMessageComposer { Avatars = avatarSnapshots });
        burst.Add(
            new ObjectsMessageComposer { OwnerNames = ownersSnapshot, FloorItems = floorSnapshot }
        );
        burst.Add(
            new ItemsMessageComposer { OwnerNames = ownersSnapshot, WallItems = wallSnapshot }
        );
        burst.Add(new RoomEntryInfoMessageComposer { RoomId = _state.RoomId, IsOwner = isOwner });

        // Sets chooserDisabled, freeFurniMovementsEnabled and invisibleFurni on the client's room
        // object, per room id. We write only the first boolean -- the client's parser guards each
        // of the other two on bytes remaining -- and none of the three is a room setting here, so
        // this pins them to their defaults rather than leaving the client's own defaults to
        // decide. The official server sends it on every entry.
        burst.Add(new HanditemConfigurationMessageComposer { IsHanditemControlBlocked = false });
        burst.Add(
            new YouAreControllerMessageComposer
            {
                RoomId = _state.RoomId,
                ControllerLevel = controllerLevel,
            }
        );

        if (isOwner)
        {
            burst.Add(new YouAreOwnerMessageComposer { RoomId = _state.RoomId });
        }

        // A hand item is not part of the Users payload, so it has to be replayed for the player
        // walking in. Ours, not Sulake's: the capture's room was empty. (The Habbo avatar effect
        // went with M1d: status visuals are the client's auras, declared in the content.)
        foreach (
            RoomPlayerAvatarSnapshot avatar in avatarSnapshots.OfType<RoomPlayerAvatarSnapshot>()
        )
        {
            if (avatar.CarryItemId != 0)
            {
                burst.Add(
                    new CarryObjectMessageComposer
                    {
                        UserId = avatar.ObjectId.Value,
                        ItemType = avatar.CarryItemId,
                    }
                );
            }
        }

        // What a player can click here (spec F3b §5): every other decor object is frozen, and the
        // client stops showing Habbo's furni info for it. Ours, after Sulake's captured order.
        if (_state.ZoneRuntime is { } zone)
        {
            burst.Add(
                new RpgZoneInteractivesMessageComposer
                {
                    Objects =
                    [
                        .. zone.Compiled.Interactives.Select(c => new RpgZoneInteractive
                        {
                            ObjectId = c.ObjectId,
                            Interaction = c.Interaction,
                        }),
                    ],
                }
            );

            // Every fighter's health, for the bars over the avatars, and the player's own when he
            // is back within the grace period (spec M1a §9.2).
            burst.AddRange(Simulation.EntryVitals(player));

            // The NPCs' markers (spec M2a §4.4) come with the avatar's creation; back within the
            // grace period none is made, so they come here.
            if (resumed && DialogueModule.MarkersFor(player) is { } markers)
            {
                burst.Add(markers);
            }
        }

        return burst.ToImmutable();
    }
}
