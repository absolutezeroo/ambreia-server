using System;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Catalog;
using Vortex.Primitives.Catalog.Providers;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Orleans.Snapshots.Players;
using Vortex.Primitives.Players.Enums;
using Vortex.Primitives.Players.Grains;
using Vortex.Protocol.Messages.Incoming.Catalog;
using Vortex.Protocol.Messages.Outgoing.Catalog;
using Vortex.Protocol.Messages.Outgoing.Handshake;
using Vortex.Protocol.Messages.Outgoing.Users;

namespace Vortex.PacketHandlers.Catalog;

public class PurchaseBasicMembershipExtensionMessageHandler(
    IGrainFactory grainFactory,
    IMembershipOfferProvider membershipOfferProvider
) : IMessageHandler<PurchaseBasicMembershipExtensionMessage>
{
    private readonly IGrainFactory _grainFactory = grainFactory;
    private readonly IMembershipOfferProvider _membershipOfferProvider = membershipOfferProvider;

    public async ValueTask HandleAsync(
        PurchaseBasicMembershipExtensionMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0)
        {
            return;
        }

        if (await SafetyLockGuard.RefusedAsync(_grainFactory, ctx, ct).ConfigureAwait(false))
        {
            return;
        }

        MembershipOffer? offer = _membershipOfferProvider.FindById(message.OfferId);
        if (offer is null || offer.IsVip)
        {
            return;
        }

        IPlayerGrain playerGrain = _grainFactory.GetPlayerGrain(ctx.PlayerId);

        MembershipPurchaseResult result = await playerGrain
            .PurchaseMembershipAsync(offer.Months, false, offer.PriceCredits, ct)
            .ConfigureAwait(false);

        if (result != MembershipPurchaseResult.Success)
        {
            await ctx.SendComposerAsync(new PurchaseErrorMessageComposer(), ct)
                .ConfigureAwait(false);
            return;
        }

        MembershipSnapshot sub = await playerGrain.GetMembershipAsync(ct).ConfigureAwait(false);

        await ctx.SendComposerAsync(
                new UserRightsMessage
                {
                    MembershipLevel = MembershipLevelType.Membership,
                    SecurityLevel = SecurityLevelType.None,
                    IsAmbassador = false,
                },
                ct
            )
            .ConfigureAwait(false);

        if (sub.IsActive)
        {
            int daysLeft = sub.DaysLeft;
            int rem = daysLeft % 31;
            await ctx.SendComposerAsync(
                    new ScrSendUserInfoMessageComposer
                    {
                        ProductName = "habbo_club",
                        DaysToPeriodEnd = rem == 0 ? 31 : rem,
                        MemberPeriods = sub.TotalMonths,
                        PeriodsSubscribedAhead = daysLeft / 31 - (rem == 0 ? 1 : 0),
                        ResponseType = 2,
                        HasEverBeenMember = sub.TotalMonths > 0 || sub.IsActive,
                        IsVIP = sub.IsVip,
                        PastMembershipDays = sub.PastMembershipDays,
                        PastVipDays = sub.PastVipDays,
                        MinutesUntilExpiration = (int)
                            (sub.ExpiresAt - DateTime.UtcNow).TotalMinutes,
                        MinutesSinceLastModified = 0,
                    },
                    ct
                )
                .ConfigureAwait(false);
        }
    }
}
