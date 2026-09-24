using System.Threading;
using System.Threading.Tasks;
using Vortex.Messages.Registry;
using Vortex.PacketHandlers.Moderation;
using Vortex.Primitives.Moderation.WordFilter;
using Vortex.Primitives.Rooms;
using Vortex.Protocol.Messages.Incoming.Navigator;
using Vortex.Protocol.Messages.Outgoing.Navigator;
using Vortex.Protocol.Messages.Outgoing.Room.Session;

namespace Vortex.PacketHandlers.Navigator;

public class CreateFlatMessageHandler(IRoomService roomService, IWordFilter wordFilter)
    : IMessageHandler<CreateFlatMessage>
{
    private readonly IRoomService _roomService = roomService;
    private readonly IWordFilter _wordFilter = wordFilter;

    public async ValueTask HandleAsync(
        CreateFlatMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        // Here and not in RoomService.CreateRoomAsync: its other caller, the onboarding's first
        // room, passes a name the server wrote ("{owner}'s room"), not one a player typed.
        string? name = await _wordFilter
            .PassAsync(ctx, message.FlatName, WordFilterSource.Room, ct)
            .ConfigureAwait(false);
        string? description = name is null
            ? null
            : await _wordFilter
                .PassAsync(ctx, message.FlatDescription, WordFilterSource.Room, ct)
                .ConfigureAwait(false);

        if (name is null || description is null)
        {
            return;
        }

        (RoomId roomId, string createdName) = await _roomService
            .CreateRoomAsync(
                name,
                description,
                message.FlatModelName,
                message.CategoryID,
                message.MaxPlayers,
                message.TradeSetting,
                ctx.PlayerId,
                ct
            )
            .ConfigureAwait(false);

        // FlatCreated alone does not move the player: the client only listens for it in its guild
        // window, to add the room to a dropdown. Walking into the room you just made is what
        // RoomForward is for -- the navigator answers it with forwardToRoom.
        await ctx.SendComposerAsync(
                new FlatCreatedMessageComposer { RoomId = roomId, Name = createdName },
                ct
            )
            .ConfigureAwait(false);

        await ctx.SendComposerAsync(new RoomForwardMessageComposer { RoomId = roomId }, ct)
            .ConfigureAwait(false);
    }
}
