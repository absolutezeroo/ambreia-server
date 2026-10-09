using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Orleans;
using Vortex.Protocol.Messages.Incoming.Rpg.Items;

namespace Vortex.PacketHandlers.Rpg.Items;

/// <summary>Claiming a parcel (spec Objets §17); the character checks there is room and answers.</summary>
public class RpgItemClaimParcelMessageHandler(IGrainFactory grainFactory)
    : IMessageHandler<RpgItemClaimParcelMessage>
{
    public async ValueTask HandleAsync(
        RpgItemClaimParcelMessage message,
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
            .ClaimParcelAsync(message.Parcel)
            .ConfigureAwait(false);
    }
}
