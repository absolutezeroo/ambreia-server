using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Orleans;
using Vortex.Protocol.Messages.Incoming.Rpg.Skills;

namespace Vortex.PacketHandlers.Rpg.Skills;

/// <summary>A slot of the action bar set (spec M1b §9); the player's own character checks and
/// answers.</summary>
public class RpgActionBarSetMessageHandler(IGrainFactory grainFactory)
    : IMessageHandler<RpgActionBarSetMessage>
{
    public async ValueTask HandleAsync(
        RpgActionBarSetMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0)
        {
            return;
        }

        await grainFactory
            .GetPlayerCharacterGrain(ctx.PlayerId)
            .SetActionBarAsync(message.Slot, message.ActionId)
            .ConfigureAwait(false);
    }
}
