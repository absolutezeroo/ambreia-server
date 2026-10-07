using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Quests;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Protocol.Messages.Incoming.Room.Chat;

namespace Vortex.PacketHandlers.Room.Chat;

public class ChatMessageHandler(IGrainFactory grainFactory) : IMessageHandler<ChatMessage>
{
    private readonly IGrainFactory _grainFactory = grainFactory;

    public async ValueTask HandleAsync(
        ChatMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx is null || ctx.PlayerId <= 0 || ctx.RoomId <= 0)
        {
            return;
        }

        IRoomAvatars roomChatGrain = _grainFactory.GetRoomAvatars(ctx.RoomId);

        await roomChatGrain
            .SendChatFromPlayerAsync(
                ctx.PlayerId,
                message.Text,
                AvatarGestureType.None,
                [],
                message.TrackingId
            )
            .ConfigureAwait(false);

        await _grainFactory
            .GetPlayerQuestGrain(ctx.PlayerId)
            .ProgressAsync(QuestTypes.Chat, 1, ct)
            .ConfigureAwait(false);
    }
}
