using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Catalog;
using Vortex.Primitives.Catalog.Providers;
using Vortex.Primitives.Catalog.Snapshots;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Orleans.Snapshots.Players;
using Vortex.Protocol.Messages.Incoming.Catalog;
using Vortex.Protocol.Messages.Outgoing.Catalog;

namespace Vortex.PacketHandlers.Catalog;

public class GetClubGiftInfoMessageHandler(
    IGrainFactory grainFactory,
    IMembershipGiftProvider membershipGiftProvider
) : IMessageHandler<GetClubGiftInfoMessage>
{
    private readonly IGrainFactory _grainFactory = grainFactory;
    private readonly IMembershipGiftProvider _membershipGiftProvider = membershipGiftProvider;

    public async ValueTask HandleAsync(
        GetClubGiftInfoMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0)
        {
            return;
        }

        MembershipSnapshot sub = await _grainFactory
            .GetPlayerGrain(ctx.PlayerId)
            .GetMembershipAsync(ct)
            .ConfigureAwait(false);

        IReadOnlyList<CatalogOfferSnapshot> offers = _membershipGiftProvider.GetAll();

        DateTime now = DateTime.UtcNow;
        int daysUntilNextGift =
            sub.IsActive && sub.NextGiftAt.HasValue && sub.NextGiftAt.Value > now
                ? (int)Math.Ceiling((sub.NextGiftAt.Value - now).TotalDays)
                : 0;

        List<MembershipGiftOfferData> giftData = offers
            .Select(o =>
            {
                bool isVipGift = o.MembershipLevel > 1;
                return new MembershipGiftOfferData
                {
                    OfferId = o.Id,
                    IsVip = isVipGift,
                    DaysRequired = 0,
                    // Only claimable with an active membership, a gift token in hand, and — for
                    // VIP-only gifts — an active VIP tier.
                    IsSelectable =
                        sub.IsActive && sub.GiftsAvailable > 0 && (!isVipGift || sub.IsVip),
                };
            })
            .ToList();

        await ctx.SendComposerAsync(
                new ClubGiftInfoEventMessageComposer
                {
                    DaysUntilNextGift = daysUntilNextGift,
                    GiftsAvailable = sub.GiftsAvailable,
                    Offers = offers,
                    GiftData = giftData,
                },
                ct
            )
            .ConfigureAwait(false);
    }
}
