using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Orleans.Snapshots.Room;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Protocol.Messages.Outgoing.Navigator;

namespace Vortex.PacketHandlers.Navigator;

/// <summary>
///     Shared "send this room's guest-room card to the requesting player" orchestration, used both
///     by GetGuestRoomMessage itself and by the server-initiated forward flows (ConvertGlobalRoomId,
///     ForwardToARandomPromotedRoom, ForwardToSomeRoom) that resolve a target room without the
///     client naming one directly. EnterRoom=true is how the client is told to proceed with its own
///     connect handshake, same as clicking a room in a search result.
/// </summary>
internal static class RoomForwardHelper
{
    public static async ValueTask SendGuestRoomResultAsync(
        IGrainFactory grainFactory,
        MessageContext ctx,
        RoomId roomId,
        bool enterRoom,
        bool roomForward,
        CancellationToken ct
    )
    {
        IRoomCore roomGrain = grainFactory.GetRoomCore(roomId);
        RoomSnapshot snapshot = await roomGrain.GetSnapshotAsync().ConfigureAwait(false);

        // Live, not persisted: whether the room is silenced right now and whether this player gets
        // the switch. Same activation as the snapshot call above, so it costs one more message to a
        // grain that is already awake.
        RoomMuteStateSnapshot muteState = await grainFactory
            .GetRoomModeration(roomId)
            .GetMuteStateAsync(ctx.PlayerId)
            .ConfigureAwait(false);

        // Drives the "you are a member" affordances on the room card; only worth a grain call when
        // the room actually belongs to a guild.
        bool isGroupMember =
            snapshot.GroupId is int groupId
            && groupId > 0
            && await grainFactory
                .GetGroupGrain(groupId)
                .GetMemberRankAsync(ctx.PlayerId, ct)
                .ConfigureAwait(false)
                is not null;

        await ctx.SendComposerAsync(
                new GetGuestRoomResultMessageComposer
                {
                    EnterRoom = enterRoom,
                    RoomInfo = snapshot,
                    RoomForward = roomForward,
                    IsGroupMember = isGroupMember,
                    AllInRoomMuted = muteState.AllInRoomMuted,
                    CanMute = muteState.CanMute,
                    OpeningConnection = false,
                },
                ct
            )
            .ConfigureAwait(false);
    }
}
