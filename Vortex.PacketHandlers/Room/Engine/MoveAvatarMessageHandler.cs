using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Action;
using Vortex.Primitives.Orleans;
using Vortex.Protocol.Messages.Incoming.Room.Engine;

namespace Vortex.PacketHandlers.Room.Engine;

public class MoveAvatarMessageHandler(IGrainFactory grainFactory)
    : IMessageHandler<MoveAvatarMessage>
{
    private readonly IGrainFactory _grainFactory = grainFactory;

    public async ValueTask HandleAsync(
        MoveAvatarMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0 || ctx.RoomId <= 0)
        {
            return;
        }

        ActionContext action = ctx.AsActionContext();

        // The tile's click listener first, then the walk: a furniture on the tile reacts to the
        // click whether or not the avatar can reach it.
        await _grainFactory
            .GetRoomMap(ctx.RoomId)
            .ClickTileAsync(action, message.TargetX, message.TargetY, ct)
            .ConfigureAwait(false);
        await _grainFactory
            .GetRoomAvatars(ctx.RoomId)
            .WalkAvatarToAsync(action, message.TargetX, message.TargetY, message.TargetZKey, ct)
            .ConfigureAwait(false);
    }
}
