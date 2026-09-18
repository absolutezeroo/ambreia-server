using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Furniture;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players.Wallet;
using Vortex.Protocol.Messages.Incoming.Room.Furniture;

namespace Vortex.PacketHandlers.Room.Furniture;

/// <summary>
/// Cashes a credit furni in. The room consumes the coin and reports what it was worth and in which
/// currency; the wallet pays it out and pushes the new balance, which is what the client redraws.
/// </summary>
/// <remarks>
/// One message, two coins. A <c>CF_*</c> furni pays credits; an <c>nft_emerald*</c> one pays
/// emeralds, and the client says so before it sends this — its redeem widget prints
/// <c>nft.creditfurni.redeem.prompt</c>, "do you want to convert it to emeralds". Which purse to pay
/// is the room's to decide, because the classname it decides from is the room's to read.
/// </remarks>
public class CreditFurniRedeemMessageHandler(IGrainFactory grainFactory)
    : IMessageHandler<CreditFurniRedeemMessage>
{
    public async ValueTask HandleAsync(
        CreditFurniRedeemMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0 || ctx.RoomId <= 0)
        {
            return;
        }

        CreditFurniPayout payout = await grainFactory
            .GetRoomFurni(ctx.RoomId)
            .RedeemCreditFurniAsync(ctx.AsActionContext(), message.ObjectId, ct)
            .ConfigureAwait(false);

        if (payout.Amount <= 0)
        {
            return;
        }

        await grainFactory
            .GetPlayerWalletGrain(ctx.PlayerId)
            .GrantCurrencyAsync(
                new CurrencyKind { CurrencyType = payout.Currency },
                payout.Amount,
                ct
            )
            .ConfigureAwait(false);
    }
}
