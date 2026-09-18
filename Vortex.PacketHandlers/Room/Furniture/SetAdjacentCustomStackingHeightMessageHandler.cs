using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Orleans;
using Vortex.Protocol.Messages.Incoming.Room.Furniture;

namespace Vortex.PacketHandlers.Room.Furniture;

/// <summary>
/// Steps a magic stack tile to the next surface on its own tile. The room decides what "next" is —
/// the client sends only the direction.
/// </summary>
public class SetAdjacentCustomStackingHeightMessageHandler(IGrainFactory grainFactory)
    : IMessageHandler<SetAdjacentCustomStackingHeightMessage>
{
    public async ValueTask HandleAsync(
        SetAdjacentCustomStackingHeightMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0 || ctx.RoomId <= 0)
        {
            return;
        }

        await grainFactory
            .GetRoomFurni(ctx.RoomId)
            .SetAdjacentCustomStackHeightAsync(
                ctx.AsActionContext(),
                message.ObjectId,
                message.Down,
                ct
            )
            .ConfigureAwait(false);
    }
}
