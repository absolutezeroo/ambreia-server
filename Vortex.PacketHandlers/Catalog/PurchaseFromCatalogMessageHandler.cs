using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Catalog.Exceptions;
using Vortex.Messages.Registry;
using Vortex.PacketHandlers.Moderation;
using Vortex.Primitives.Catalog;
using Vortex.Primitives.Catalog.Enums;
using Vortex.Primitives.Catalog.Grains;
using Vortex.Primitives.Catalog.Providers;
using Vortex.Primitives.Catalog.Snapshots;
using Vortex.Primitives.Moderation.WordFilter;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Orleans.Snapshots.Players;
using Vortex.Primitives.Players.Enums;
using Vortex.Primitives.Players.Enums.Wallet;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Players.Wallet;
using Vortex.Protocol.Messages.Incoming.Catalog;
using Vortex.Protocol.Messages.Outgoing.Catalog;
using Vortex.Protocol.Messages.Outgoing.Handshake;
using Vortex.Protocol.Messages.Outgoing.Users;

namespace Vortex.PacketHandlers.Catalog;

public class PurchaseFromCatalogMessageHandler(
    IGrainFactory grainFactory,
    ICatalogService catalogService,
    ICatalogClubOfferProvider clubOfferProvider,
    IWordFilter wordFilter
) : IMessageHandler<PurchaseFromCatalogMessage>
{
    private readonly IWordFilter _wordFilter = wordFilter;
    private readonly IGrainFactory _grainFactory = grainFactory;
    private readonly ICatalogService _catalogService = catalogService;
    private readonly ICatalogClubOfferProvider _clubOfferProvider = clubOfferProvider;

    public async ValueTask HandleAsync(
        PurchaseFromCatalogMessage message,
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

        CatalogSnapshot snapshot = _catalogService.GetCatalogSnapshot(CatalogType.Normal);

        if (
            snapshot.PagesById.TryGetValue(message.PageId, out CatalogPageSnapshot? page)
            && page.Layout == CatalogPageLayout.ClubBuy
        )
        {
            await HandleClubPurchaseAsync(message, ctx, ct);
            return;
        }

        await HandleCatalogPurchaseAsync(message, ctx, ct);
    }

    private async ValueTask HandleClubPurchaseAsync(
        PurchaseFromCatalogMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        ClubOffer? clubOffer = _clubOfferProvider.FindById(message.OfferId);

        if (clubOffer is null)
        {
            await ctx.SendComposerAsync(
                    new PurchaseNotAllowedMessageComposer
                    {
                        ErrorType = CatalogPurchaseErrorType.OfferNotFound,
                    },
                    ct
                )
                .ConfigureAwait(false);
            return;
        }

        if (clubOffer.PriceCredits > 0)
        {
            IPlayerWalletGrain wallet = _grainFactory.GetPlayerWalletGrain(ctx.PlayerId);
            int credits = await wallet
                .GetAmountForCurrencyAsync(
                    new CurrencyKind { CurrencyType = CurrencyType.Credits },
                    ct
                )
                .ConfigureAwait(false);

            if (credits < clubOffer.PriceCredits)
            {
                await ctx.SendComposerAsync(
                        new NotEnoughBalanceMessageComposer
                        {
                            NotEnoughCredits = true,
                            NotEnoughActivityPoints = false,
                            ActivityPointType = 0,
                        },
                        ct
                    )
                    .ConfigureAwait(false);
                return;
            }
        }

        IPlayerGrain playerGrain = _grainFactory.GetPlayerGrain(ctx.PlayerId);
        ClubPurchaseResult purchaseResult = await playerGrain
            .PurchaseClubAsync(clubOffer.Months, clubOffer.IsVip, clubOffer.PriceCredits, ct)
            .ConfigureAwait(false);

        if (purchaseResult == ClubPurchaseResult.NotEnoughCredits)
        {
            await ctx.SendComposerAsync(
                    new NotEnoughBalanceMessageComposer
                    {
                        NotEnoughCredits = true,
                        NotEnoughActivityPoints = false,
                        ActivityPointType = 0,
                    },
                    ct
                )
                .ConfigureAwait(false);
            return;
        }

        if (purchaseResult != ClubPurchaseResult.Success)
        {
            await ctx.SendComposerAsync(new PurchaseErrorMessageComposer(), ct)
                .ConfigureAwait(false);
            return;
        }

        ClubSubscriptionSnapshot sub = await playerGrain
            .GetClubSubscriptionAsync(ct)
            .ConfigureAwait(false);

        ClubLevelType clubLevel = sub.IsActive
            ? (sub.IsVip ? ClubLevelType.Vip : ClubLevelType.Club)
            : ClubLevelType.None;

        DateTime now = DateTime.UtcNow;
        DateTime baseDate = sub.ExpiresAt > now ? sub.ExpiresAt : now;

        List<ClubOffer> refreshedOffers = new List<ClubOffer>();
        foreach (ClubOffer offer in _clubOfferProvider.GetAll())
        {
            DateTime expiry = baseDate.AddMonths(offer.Months).AddDays(offer.ExtraDays);
            refreshedOffers.Add(
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
                new UserRightsMessage
                {
                    ClubLevel = clubLevel,
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
                        PastClubDays = sub.PastClubDays,
                        PastVipDays = sub.PastVipDays,
                        MinutesUntilExpiration = (int)
                            (sub.ExpiresAt - DateTime.UtcNow).TotalMinutes,
                        MinutesSinceLastModified = 0,
                    },
                    ct
                )
                .ConfigureAwait(false);
        }

        await ctx.SendComposerAsync(
                new HabboClubOffersMessageComposer
                {
                    Offers = refreshedOffers,
                    Source = ClubOfferRequestSourceType.Unknown,
                },
                ct
            )
            .ConfigureAwait(false);
    }

    private async ValueTask HandleCatalogPurchaseAsync(
        PurchaseFromCatalogMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        // Before the grain, which debits: a pet (or bot) refused its name costs nothing.
        // PurchaseError closes the catalogue's pending purchase; the bubble says why.
        string? extraParam = await _wordFilter
            .PassPurchaseExtraAsync(ctx, message.ExtraParam, ct)
            .ConfigureAwait(false);

        if (extraParam is null)
        {
            await ctx.SendComposerAsync(new PurchaseErrorMessageComposer(), ct)
                .ConfigureAwait(false);

            return;
        }

        try
        {
            ICatalogPurchaseGrain purchaseGrain = _grainFactory.GetCatalogPurchaseGrain(
                ctx.PlayerId
            );
            CatalogOfferSnapshot offer = await purchaseGrain
                .PurchaseOfferFromCatalogAsync(
                    CatalogType.Normal,
                    message.OfferId,
                    extraParam,
                    message.Quantity,
                    ct
                )
                .ConfigureAwait(false);

            await ctx.SendComposerAsync(new PurchaseOKMessageComposer { Offer = offer }, ct)
                .ConfigureAwait(false);
        }
        catch (CatalogPurchaseException ex)
        {
            await CatalogPurchaseErrorResponder.SendAsync(ctx, ex, ct).ConfigureAwait(false);
        }
    }
}
