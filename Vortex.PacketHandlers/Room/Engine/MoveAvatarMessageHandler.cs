using System.Threading;
using System.Threading.Tasks;
using Vortex.Messages.Registry;
using Vortex.Primitives.Rooms;
using Vortex.Protocol.Messages.Incoming.Room.Engine;

namespace Vortex.PacketHandlers.Room.Engine;

public class MoveAvatarMessageHandler(IRoomService roomService) : IMessageHandler<MoveAvatarMessage>
{
    private readonly IRoomService _roomService = roomService;

    public async ValueTask HandleAsync(
        MoveAvatarMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        await _roomService
            .ClickTileAsync(
                ctx.AsActionContext(),
                message.TargetX,
                message.TargetY,
                message.TargetZKey,
                ct
            )
            .ConfigureAwait(false);
    }
}
