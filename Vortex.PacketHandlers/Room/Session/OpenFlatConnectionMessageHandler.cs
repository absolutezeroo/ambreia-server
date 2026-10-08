using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Orleans;
using Vortex.Protocol.Messages.Incoming.Room.Session;

namespace Vortex.PacketHandlers.Room.Session;

public class OpenFlatConnectionMessageHandler(IGrainFactory grainFactory)
    : IMessageHandler<OpenFlatConnectionMessage>
{
    private readonly IGrainFactory _grainFactory = grainFactory;

    public async ValueTask HandleAsync(
        OpenFlatConnectionMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        // A session not yet signed in carries no player: it must not activate a presence grain,
        // nor a room of its choosing.
        if (ctx.PlayerId <= 0)
        {
            return;
        }

        await _grainFactory
            .GetPlayerPresenceGrain(ctx.PlayerId)
            .EnterRoomAsync(message.RoomId, message.Password, ct)
            .ConfigureAwait(false);
    }
}
