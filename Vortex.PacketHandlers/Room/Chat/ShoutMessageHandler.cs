using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Protocol.Messages.Incoming.Room.Chat;

namespace Vortex.PacketHandlers.Room.Chat;

public class ShoutMessageHandler : IMessageHandler<ShoutMessage>
{
    private readonly IGrainFactory _grainFactory;

    public ShoutMessageHandler(IGrainFactory grainFactory) => _grainFactory = grainFactory;

    public async ValueTask HandleAsync(
        ShoutMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (
            ctx is null
            || ctx.PlayerId <= 0
            || ctx.RoomId <= 0
            || string.IsNullOrWhiteSpace(message.Text)
        )
        {
            return;
        }

        IRoomAvatars roomGrain = _grainFactory.GetRoomAvatars(ctx.RoomId);
        await roomGrain
            .SendChatFromPlayerAsync(
                ctx.PlayerId,
                message.Text,
                AvatarGestureType.None,
                [],
                0,
                chatType: RoomChatType.Shout
            )
            .ConfigureAwait(false);
    }
}
