using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Collectibles;
using Vortex.Primitives.Collectibles.Grains;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;
using Vortex.Protocol.Messages.Incoming.Collectibles;
using Vortex.Protocol.Messages.Outgoing.Collectibles;

namespace Vortex.PacketHandlers.Collectibles;

/// <summary>
/// The Transfer tab's "move my Relics to that address" — the door out of the hotel.
/// </summary>
/// <remarks>
/// <para>
/// It moves the player's <em>whole</em> collection at once, because that is all the message carries:
/// one destination address and no item list. Handing over a single Relic is done in the trade window
/// instead, per item and with the confirmation both sides already know.
/// </para>
/// <para>
/// This used to refuse outright, on the grounds that there was no chain to move anything onto and
/// that the destination list was ambiguous. Neither holds any more: the hotel runs its own chain, and
/// the addresses a player may send to are the ones they PROVED on the website, which is what
/// <see cref="IPlayerMintGrain.TransferAssetsAsync" /> checks before it mints anything. Without that
/// check the destination would be whatever the client sent, and minting to it would be a way to hand
/// a stranger somebody else's collection.
/// </para>
/// <para>
/// The tab is still gated by <c>collectibles.transfer.enabled</c> in the client's external
/// variables, so a hotel with no chain switches the whole thing off there rather than relying on
/// this to refuse.
/// </para>
/// </remarks>
public class TransferNftAssetsMessageHandler(IGrainFactory grainFactory)
    : IMessageHandler<TransferNftAssetsMessage>
{
    private readonly IGrainFactory _grainFactory = grainFactory;

    public async ValueTask HandleAsync(
        TransferNftAssetsMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0)
        {
            return;
        }

        IPlayerMintGrain mint = _grainFactory.GetPlayerMintGrain(new PlayerId(ctx.PlayerId));

        NftTransferResult result = await mint.TransferAssetsAsync(
                message.Wallet ?? string.Empty,
                ct
            )
            .ConfigureAwait(false);

        // The outcome's own numbering goes on the wire: the client prints the code into
        // "collectibles.transfer.error" and shows nothing else, and it reads zero as success — which
        // is exactly NftTransferOutcome.Transferred.
        await ctx.SendComposerAsync(
                new NftTransferAssetsResultMessageComposer { ResultCode = (short)result.Outcome },
                ct
            )
            .ConfigureAwait(false);

        if (result.Outcome != NftTransferOutcome.Transferred)
        {
            return;
        }

        // Re-sent unasked, because nothing else will ask. The inventory's Collectibles tab treats
        // its list as authoritative once it has one, so a Relic that just left the hotel would go on
        // being offered for trade until the player reopened the client.
        ImmutableArray<CollectibleAssetSnapshot> assets = await mint.GetAssetsAsync(ct)
            .ConfigureAwait(false);

        await ctx.SendComposerAsync(
                new TradeNftAssetInventoryMessageComposer { Assets = assets },
                ct
            )
            .ConfigureAwait(false);
    }
}
