using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Logging;
using Vortex.Logging.Extensions;
using Vortex.Primitives;
using Vortex.Primitives.Action;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Avatars;
using Vortex.Primitives.Rooms.Object.Furniture;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Primitives.Rooms.Object.Furniture.Wall;
using Vortex.Primitives.Rooms.Object.Logic;
using Vortex.Primitives.Rooms.Snapshots.Furniture;
using Vortex.Protocol.Messages.Outgoing.Room.Engine;
using Vortex.Rooms.Object.Avatars.Player;
using Vortex.Rooms.Object.Furniture.Floor;
using Vortex.Rooms.Object.Furniture.Wall;

namespace Vortex.Rooms.Grains.Modules;

public sealed partial class RoomObjectModule(RoomGrain roomGrain)
{
    private readonly RoomGrain _roomGrain = roomGrain;

    public Task<ImmutableDictionary<PlayerId, string>> GetAllOwnersAsync(CancellationToken ct) =>
        Task.FromResult(_roomGrain._state.OwnerNamesById.ToImmutableDictionary());

    public async Task<bool> AttatchObjectAsync(IRoomObject roomObject, CancellationToken ct)
    {
        switch (roomObject)
        {
            case IRoomItem item:
                return await AttachItemAsync(item, ct);
            case IRoomAvatar avatar:
            {
                if (!_roomGrain._state.AvatarsByObjectId.TryAdd(avatar.ObjectId, avatar))
                {
                    throw new VortexException(VortexErrorCodeEnum.AvatarNotFound);
                }

                await AttatchLogicAsync(avatar, ct);
                await _roomGrain.AvatarModule.ProcessNextAvatarStepAsync(avatar, ct);

                _roomGrain
                    .SendComposerToRoomAsync(
                        new UsersMessageComposer { Avatars = [avatar.GetSnapshot()] }
                    )
                    .LogAndForget(_roomGrain._logger, "Failed to broadcast avatar attach.");
                break;
            }
            default:
                return false;
        }

        return true;
    }

    /// <summary>
    /// Publishes an item into the room's live state, or leaves the room exactly as it found it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The item is in <c>ItemsById</c> from the first line, and everything after it can fail: the
    /// owner-name lookup is a grain call, the logic comes from a provider that can refuse the
    /// definition's logic name, <c>OnAttachAsync</c> is behaviour code, and the map add answers a
    /// bool. All of that used to return <c>false</c> or throw with the item left published --
    /// without a logic, without a footprint, invisible to the client because the add composer is
    /// only broadcast on success. The item was then a ghost: <c>ItemsById</c> answered for it, so
    /// every use site dereferenced a null logic, and <c>TryAdd</c> refused to place it again for
    /// the life of the activation.
    /// </para>
    /// <para>
    /// So the publication is undone on both exits. A caller that gets <c>false</c>, or an exception,
    /// can be sure the room is as it was.
    /// </para>
    /// </remarks>
    private async Task<bool> AttachItemAsync(IRoomItem item, CancellationToken ct)
    {
        if (!_roomGrain._state.ItemsById.TryAdd(item.ObjectId, item))
        {
            throw new VortexException(VortexErrorCodeEnum.FloorItemNotFound);
        }

        try
        {
            if (!_roomGrain._state.OwnerNamesById.TryGetValue(item.OwnerId, out string? value))
            {
                string ownerName = await _roomGrain
                    ._grainFactory.GetPlayerDirectoryGrain()
                    .GetPlayerNameAsync(item.OwnerId, ct);

                value = ownerName;
                _roomGrain._state.OwnerNamesById[item.OwnerId] = value;
            }

            item.SetOwnerName(value ?? string.Empty);
            item.SetAction(objectId => _roomGrain._state.DirtyItemIds.Add(objectId));

            if (!await AttatchLogicAsync(item, ct) || !_roomGrain.MapModule.AddItem(item))
            {
                RollBackItemAttach(item);

                return false;
            }

            return true;
        }
        catch
        {
            RollBackItemAttach(item);

            throw;
        }
    }

    /// <summary>
    /// Undoes a partial attach. Takes the footprint back off the map before the identity, because
    /// <c>RemoveItem</c> reads the item's own coordinates to find the tiles it was registered on.
    /// </summary>
    /// <remarks>
    /// Every step this reverses is optional -- the map add may never have run, the logic may never
    /// have been created -- and every one of them is safe to reverse twice: removing an id from a
    /// tile stack that does not hold it, or from an index that never indexed it, is a no-op. That
    /// is what lets one undo serve every failure point instead of a journal per step.
    /// </remarks>
    private void RollBackItemAttach(IRoomItem item)
    {
        _roomGrain.MapModule.RemoveItem(item);

        DetachItemFromLiveState(item);
    }

    /// <summary>
    /// Takes an item out of the room's identity table and logic index, and stops it marking itself
    /// dirty. The caller owns the spatial removal and the order it runs in.
    /// </summary>
    /// <remarks>
    /// These three lines were written out by hand at each place an item leaves a room -- a pickup,
    /// a definition swap -- in the same order every time, which is copies of one invariant and no
    /// way to notice when one drifts. Nothing here
    /// awaits, so no other turn can see the room holding an item in two of the three.
    ///
    /// The spatial removal is deliberately not folded in: each caller runs it at a different point
    /// relative to its own hooks, and a wired box's <c>OnPickupAsync</c> resolves its pile from the
    /// tile it used to sit on, so moving that step would change what those hooks see.
    /// </remarks>
    public void DetachItemFromLiveState(IRoomItem item)
    {
        item.SetAction(null);

        _roomGrain._state.ItemsById.Remove(item.ObjectId);
        _roomGrain._state.ItemIndex.OnItemDetached(item);
    }

    public async Task<bool> RemoveObjectAsync(
        ActionContext ctx,
        IRoomObject roomObject,
        CancellationToken ct,
        int pickerId = -1
    )
    {
        switch (roomObject)
        {
            case IRoomItem item:
            {
                if (!_roomGrain.MapModule.RemoveItem(item))
                {
                    return false;
                }

                await _roomGrain.SendComposerToRoomAsync(item.GetRemoveComposer(pickerId));

                // The hooks run between the footprint coming off the map and the identity going,
                // and that gap is two awaits wide. They are behaviour code -- a logic can rewrite its
                // ExtraData or tell the room its pile changed -- so any of them can throw, and when
                // one did the item was left off the map but still in ItemsById and still in the
                // logic index. A game holding the index bucket kept
                // acting on furniture that was no longer anywhere, and the tile it used to stand on
                // had already forgotten it.
                //
                // Whatever the hooks do, the three representations end up agreeing. The exception
                // still reaches the caller, which is what stops the pickup crediting an inventory
                // for an item whose hooks did not finish -- and the row still says this room, so a
                // reload brings it back rather than losing it.
                try
                {
                    await item.Logic.OnDetachAsync(ct);
                    await item.Logic.OnPickupAsync(ctx, ct);
                }
                finally
                {
                    DetachItemFromLiveState(item);
                }

                RoomItemSnapshot snapshot = item.GetSnapshot();

                // The last thing the item was: its final extra data, its rotation. Where it went is
                // not this queue's business -- the pickup moved the row itself, before the client
                // was told anything.
                await _roomGrain
                    ._grainFactory.GetRoomPersistenceGrain(_roomGrain.RoomId)
                    .EnqueueDirtyItemAsync(_roomGrain.RoomId, snapshot, ct);
                break;
            }
            case IRoomAvatar avatar:
            {
                await _roomGrain.AvatarModule.StopWalkingAsync(avatar, ct);

                _roomGrain.MapModule.RemoveAvatar(avatar, false);

                await avatar.Logic.OnDetachAsync(ct);

                await _roomGrain.SendComposerToRoomAsync(
                    new UserRemoveMessageComposer { ObjectId = avatar.ObjectId }
                );

                _roomGrain._state.AvatarsByObjectId.Remove(avatar.ObjectId);
                break;
            }
        }

        return true;
    }

    private async Task<bool> AttatchLogicAsync(IRoomObject roomObject, CancellationToken ct)
    {
        if (roomObject.Logic is not null)
        {
            return false;
        }

        string logicType = string.Empty;
        IRoomObjectContext? ctx = null;

        switch (roomObject)
        {
            case IRoomPlayer player:
                logicType = "default_avatar";
                ctx = new RoomPlayerContext(_roomGrain, player);
                break;
            case IRoomFloorItem floor:
                logicType = floor.Definition.LogicName;
                ctx = new RoomFloorItemContext(_roomGrain, floor);
                break;
            case IRoomWallItem wall:
                logicType = wall.Definition.LogicName;
                ctx = new RoomWallItemContext(_roomGrain, wall);
                break;
        }

        if (string.IsNullOrWhiteSpace(logicType) || ctx is null)
        {
            return false;
        }

        IRoomObjectLogic logic = _roomGrain._logicProvider.CreateLogicInstance(logicType, ctx);

        roomObject.SetLogic(logic);

        if (roomObject is IRoomItem indexed)
        {
            _roomGrain._state.ItemIndex.OnLogicAttached(indexed);
        }

        await logic.OnAttachAsync(ct);

        return true;
    }
}
