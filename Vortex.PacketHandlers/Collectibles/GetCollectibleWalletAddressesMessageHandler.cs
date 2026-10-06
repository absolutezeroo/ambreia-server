using System.Collections.Immutable;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;
using Vortex.Protocol.Messages.Incoming.Collectibles;
using Vortex.Protocol.Messages.Outgoing.Collectibles;

namespace Vortex.PacketHandlers.Collectibles;

/// <summary>
/// Which wallets the player has.
///
/// The Stardust one is a keyless placeholder (<c>0x</c> plus the player id in hex): there is no
/// chain, and the client only needs a non-empty opaque value.
///
/// <para>
/// The address has to be non-empty either way. The client pushes the Stardust address into its
/// wallet list only when it is not empty, and both <c>CollectionsTab</c> and <c>RewardClaimsTab</c>
/// only ever leave their loading state from a message that is itself only requested per wallet. With
/// an empty list neither request is sent, so neither tab ever becomes ready — the Flash client
/// behaves the same way, so that is a server gap, not a client bug.
/// </para>
///
/// <para>
/// Self-custody wallets — the addresses <c>CollectibleWalletAddressesMessageEventParser</c> reads
/// AFTER the Stardust one — are linked on the website, not here: the game client has no interface
/// for connecting one, which is why habbo.com carries <c>/settings/wallet</c>. That page exists now,
/// and this is where what it writes reaches the game. Every one of them is an address whose holder
/// signed to prove it, which is what lets the Transfer tab offer it as a destination at all.
/// </para>
/// </summary>
public class GetCollectibleWalletAddressesMessageHandler(IGrainFactory grainFactory)
    : IMessageHandler<GetCollectibleWalletAddressesMessage>
{
    private readonly IGrainFactory _grainFactory = grainFactory;

    public async ValueTask HandleAsync(
        GetCollectibleWalletAddressesMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0)
        {
            return;
        }

        ImmutableArray<string> linked = await _grainFactory
            .GetPlayerMintGrain(new PlayerId(ctx.PlayerId))
            .GetLinkedWalletsAsync(ct)
            .ConfigureAwait(false);

        await ctx.SendComposerAsync(
                new CollectibleWalletAddressesMessageComposer
                {
                    StardustWalletAddress =
                        "0x"
                        + ctx.PlayerId.Value.ToString("x", CultureInfo.InvariantCulture)
                            .PadLeft(40, '0'),
                    WalletAddresses = linked,
                },
                ct
            )
            .ConfigureAwait(false);
    }
}
