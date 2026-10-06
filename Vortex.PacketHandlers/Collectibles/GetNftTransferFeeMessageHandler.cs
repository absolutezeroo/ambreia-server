using System.Threading;
using System.Threading.Tasks;
using Vortex.Messages.Registry;
using Vortex.Protocol.Messages.Incoming.Collectibles;
using Vortex.Protocol.Messages.Outgoing.Collectibles;

namespace Vortex.PacketHandlers.Collectibles;

/// <summary>
/// What moving a player's Relics out to their own wallet costs, in silver.
/// </summary>
/// <remarks>
/// <para>
/// Answering matters more than the answer. This handler used to return without sending anything,
/// and the collectibles interface waits on every one of these -- so silence left it loading rather
/// than showing a price.
/// </para>
/// <para>
/// Always zero: there is no chain, so nothing to charge for, and the transfer itself refuses.
/// </para>
/// </remarks>
public class GetNftTransferFeeMessageHandler() : IMessageHandler<GetNftTransferFeeMessage>
{
    public async ValueTask HandleAsync(
        GetNftTransferFeeMessage message,
        MessageContext ctx,
        CancellationToken ct
    ) =>
        await ctx.SendComposerAsync(new NftTransferFeeMessageComposer { Fee = 0 }, ct)
            .ConfigureAwait(false);
}
