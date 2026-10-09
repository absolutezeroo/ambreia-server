using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Orleans;
using Vortex.Protocol.Messages.Incoming.Rpg.Interaction;

namespace Vortex.PacketHandlers.Rpg.Interaction;

/// <summary>A target chosen (spec F3b §6); the player's room checks and answers.</summary>
public class RpgTargetSelectMessageHandler(IGrainFactory grainFactory)
    : IMessageHandler<RpgTargetSelectMessage>
{
    public async ValueTask HandleAsync(
        RpgTargetSelectMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0 || ctx.RoomId <= 0)
        {
            return;
        }

        await grainFactory
            .GetRoomTargets(ctx.RoomId)
            .SelectTargetAsync(ctx.AsActionContext(), message.ObjectId, ct)
            .ConfigureAwait(false);
    }
}
