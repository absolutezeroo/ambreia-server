using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Orleans;
using Vortex.Protocol.Messages.Incoming.Rpg.Items;

namespace Vortex.PacketHandlers.Rpg.Items;

/// <summary>A stack split (spec Objets §17); the character checks and answers.</summary>
public class RpgItemSplitMessageHandler(IGrainFactory grainFactory)
    : IMessageHandler<RpgItemSplitMessage>
{
    public async ValueTask HandleAsync(
        RpgItemSplitMessage message,
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
            .SplitItemAsync(message.From, message.Quantity, message.To)
            .ConfigureAwait(false);
    }
}
