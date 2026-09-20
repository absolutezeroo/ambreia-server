using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Camera;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Orleans.Snapshots.Room;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Protocol.Messages.Incoming.Camera;
using Vortex.Protocol.Messages.Outgoing.Camera;

namespace Vortex.PacketHandlers.Camera;

/// <summary>
/// The same shot, kept as the room's picture.
/// </summary>
/// <remarks>
/// <para>
/// The one thing this does that <see cref="RenderRoomMessageHandler"/> does not is ask who is
/// sending. A photo is the player's own and costs them nothing but their allowance; a thumbnail
/// replaces what everyone browsing the navigator sees of somebody's room, so it is the owner's to
/// replace. The client only offers the camera to a player with the
/// <c>NAVIGATOR_ROOM_THUMBNAIL_CAMERA</c> perk standing in their own room, which is a courtesy, not
/// a check — nothing stops a modified client from sending this in a stranger's room.
/// </para>
/// <para>
/// An answer always goes out, as with the photo: the widget destroys itself on this message and
/// waits forever without one.
/// </para>
/// </remarks>
public class RenderRoomThumbnailMessageHandler(IGrainFactory grainFactory)
    : IMessageHandler<RenderRoomThumbnailMessage>
{
    public async ValueTask HandleAsync(
        RenderRoomThumbnailMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0 || ctx.RoomId <= 0)
        {
            return;
        }

        RoomSnapshot room = await grainFactory
            .GetRoomCore(ctx.RoomId)
            .GetSnapshotAsync()
            .ConfigureAwait(false);

        CameraRenderRefusal refusal =
            room.OwnerId == ctx.PlayerId
                ? await grainFactory
                    .GetPlayerCameraGrain(ctx.PlayerId)
                    .RenderThumbnailAsync(ctx.RoomId, message.Payload, ct)
                    .ConfigureAwait(false)
                // Not theirs. Nothing is drawn, and the client is told no in the only word it has for
                // one that is not "you have taken too many".
                : CameraRenderRefusal.WrongRoom;

        await ctx.SendComposerAsync(
                new ThumbnailStatusMessageComposer
                {
                    Ok = refusal == CameraRenderRefusal.None,
                    RenderLimitHit = refusal == CameraRenderRefusal.RateLimited,
                },
                ct
            )
            .ConfigureAwait(false);
    }
}
