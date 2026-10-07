using System;
using System.Collections.Generic;
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

public class GetClubOffersMessageHandler(
    IGrainFactory grainFactory,
    IMembershipOfferProvider membershipOfferProvider
) : IMessageHandler<GetClubOffersMessage>
{
    private readonly IGrainFactory _grainFactory = grainFactory;
    private readonly IMembershipOfferProvider _membershipOfferProvider = membershipOfferProvider;

    public async ValueTask HandleAsync(
        GetClubOffersMessage message,
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

        DateTime now = DateTime.UtcNow;
        DateTime baseDate = sub.IsActive && sub.ExpiresAt > now ? sub.ExpiresAt : now;

        List<MembershipOffer> personalizedOffers = new List<MembershipOffer>();
        foreach (MembershipOffer offer in _membershipOfferProvider.GetAll())
        {
            DateTime expiry = baseDate.AddMonths(offer.Months).AddDays(offer.ExtraDays);
            personalizedOffers.Add(
                offer with
                {
                    DaysLeftAfterPurchase = (int)(expiry - now).TotalDays,
                    Year = expiry.Year,
                    Month = expiry.Month,
                    Day = expiry.Day,
                }
            );
        }

        await ctx.SendComposerAsync(
                new HabboClubOffersMessageComposer
                {
                    Offers = personalizedOffers,
                    Source = message.RequestSource,
                },
                ct
            )
            .ConfigureAwait(false);
    }
}
