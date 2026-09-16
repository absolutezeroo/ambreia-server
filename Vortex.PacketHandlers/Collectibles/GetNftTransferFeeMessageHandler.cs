using System.Threading;
using System.Threading.Tasks;
using Vortex.Messages.Registry;
using Vortex.Primitives.Chain;
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
/// Zero when no chain is configured, which is honest rather than a placeholder: with the door shut
/// there is nothing to charge for, and the transfer itself refuses.
/// </para>
/// </remarks>
public class GetNftTransferFeeMessageHandler(IChainService chain)
    : IMessageHandler<GetNftTransferFeeMessage>
{
    private readonly IChainService _chain = chain;

    public async ValueTask HandleAsync(
        GetNftTransferFeeMessage message,
        MessageContext ctx,
        CancellationToken ct
    ) =>
        await ctx.SendComposerAsync(
                new NftTransferFeeMessageComposer
                {
                    Fee = _chain.Enabled ? _chain.RelicTransferFeeSilver : 0,
                },
                ct
            )
            .ConfigureAwait(false);
}
