using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Orleans;
using Vortex.Protocol.Messages.Incoming.Rpg.Skills;

namespace Vortex.PacketHandlers.Rpg.Skills;

/// <summary>The player stops his own cast (spec M1b §6.4); his room passes it on.</summary>
public class RpgCastCancelMessageHandler(IGrainFactory grainFactory)
    : IMessageHandler<RpgCastCancelMessage>
{
    public async ValueTask HandleAsync(
        RpgCastCancelMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0 || ctx.RoomId <= 0)
        {
            return;
        }

        await grainFactory
            .GetRoomCombat(ctx.RoomId)
            .CancelCastAsync(ctx.AsActionContext(), ct)
            .ConfigureAwait(false);
    }
}
