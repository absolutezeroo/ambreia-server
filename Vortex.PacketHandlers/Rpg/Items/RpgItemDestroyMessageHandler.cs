using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Orleans;
using Vortex.Protocol.Messages.Incoming.Rpg.Items;

namespace Vortex.PacketHandlers.Rpg.Items;

/// <summary>Destroying from the bag (spec Objets §17); the character checks, writes and answers.</summary>
public class RpgItemDestroyMessageHandler(IGrainFactory grainFactory)
    : IMessageHandler<RpgItemDestroyMessage>
{
    public async ValueTask HandleAsync(
        RpgItemDestroyMessage message,
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
            .DestroyItemAsync(message.BagSlot, message.Quantity)
            .ConfigureAwait(false);
    }
}
