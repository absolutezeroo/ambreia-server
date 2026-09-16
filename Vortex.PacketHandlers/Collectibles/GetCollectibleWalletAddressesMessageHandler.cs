using System.Threading;
using System.Threading.Tasks;
using Vortex.Messages.Registry;
using Vortex.Primitives.Crypto;
using Vortex.Protocol.Messages.Incoming.Collectibles;
using Vortex.Protocol.Messages.Outgoing.Collectibles;

namespace Vortex.PacketHandlers.Collectibles;

/// <summary>
/// Which wallets the player has.
///
/// The Stardust one is the hotel's own, held on the player's behalf, and
/// <see cref="IHotelWalletService" /> derives it from the hotel's master seed — a real secp256k1
/// key, so the address is one a chain can actually be asked about. With no seed configured that
/// same service answers the keyless placeholder this handler used to build itself, which is what
/// keeps a hotel that wants no chain running unchanged.
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
/// for connecting one, which is why habbo.com carries <c>/settings/wallet</c>. They arrive through
/// this same composer once that page exists.
/// </para>
/// </summary>
public class GetCollectibleWalletAddressesMessageHandler(IHotelWalletService wallets)
    : IMessageHandler<GetCollectibleWalletAddressesMessage>
{
    private readonly IHotelWalletService _wallets = wallets;

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

        await ctx.SendComposerAsync(
                new CollectibleWalletAddressesMessageComposer
                {
                    StardustWalletAddress = _wallets.GetAddress(ctx.PlayerId),
                },
                ct
            )
            .ConfigureAwait(false);
    }
}
