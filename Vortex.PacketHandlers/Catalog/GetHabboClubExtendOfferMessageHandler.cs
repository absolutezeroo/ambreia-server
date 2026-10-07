using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Catalog;
using Vortex.Primitives.Catalog.Providers;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Orleans.Snapshots.Players;
using Vortex.Protocol.Messages.Incoming.Catalog;
using Vortex.Protocol.Messages.Outgoing.Catalog;

namespace Vortex.PacketHandlers.Catalog;

public class GetHabboClubExtendOfferMessageHandler(
    IGrainFactory grainFactory,
    IMembershipOfferProvider membershipOfferProvider
) : IMessageHandler<GetHabboClubExtendOfferMessage>
{
    private readonly IGrainFactory _grainFactory = grainFactory;
    private readonly IMembershipOfferProvider _membershipOfferProvider = membershipOfferProvider;

    public async ValueTask HandleAsync(
        GetHabboClubExtendOfferMessage message,
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

        List<MembershipOffer> offerPool = _membershipOfferProvider
            .GetAll()
            .Where(x => x.IsVip == sub.IsVip)
            .ToList();

        // Keep the semantics of a 1-month renewal offer when possible, and fallback to the
        // first matching tier offer if that data is missing.
        MembershipOffer? baseOffer =
            offerPool.FirstOrDefault(x => x.Months == 1) ?? offerPool.FirstOrDefault();

        if (baseOffer is null)
        {
            return;
        }

        DateTime now = DateTime.UtcNow;
        DateTime baseDate = sub.IsActive && sub.ExpiresAt > now ? sub.ExpiresAt : now;
        DateTime expiry = baseDate.AddMonths(baseOffer.Months).AddDays(baseOffer.ExtraDays);

        MembershipOffer personalizedOffer = baseOffer with
        {
            DaysLeftAfterPurchase = (int)(expiry - now).TotalDays,
            Year = expiry.Year,
            Month = expiry.Month,
            Day = expiry.Day,
        };

        await ctx.SendComposerAsync(
                new HabboClubExtendOfferMessageComposer
                {
                    Offer = personalizedOffer,
                    OriginalPricePerMonth = baseOffer.PriceCredits,
                    OriginalActivityPointPricePerMonth = baseOffer.PriceActivityPoints,
                    OriginalActivityPointType = baseOffer.PriceActivityPointType,
                    SubscriptionDaysLeft = sub.DaysLeft,
                },
                ct
            )
            .ConfigureAwait(false);
    }
}
