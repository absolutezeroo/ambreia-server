using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Orleans;
using Vortex.Protocol.Messages.Incoming.Users;

namespace Vortex.PacketHandlers.Users;

public class RespectUserMessageHandler(IGrainFactory grainFactory)
    : IMessageHandler<RespectUserMessage>
{
    private readonly IGrainFactory _grainFactory = grainFactory;

    public async ValueTask HandleAsync(
        RespectUserMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx is null || ctx.PlayerId <= 0 || ctx.RoomId <= 0 || message.UserId <= 0)
        {
            return;
        }

        // The budget belongs to the giver's grain (respect.daily_limit), not to this handler: it is
        // the same number the user object handed the client at login, and only one of the two can
        // be authoritative.
        await _grainFactory
            .GetRoomAvatars(ctx.RoomId)
            .RespectPlayerAsync(ctx.AsActionContext(), message.UserId, ct)
            .ConfigureAwait(false);
    }
}
