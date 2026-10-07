using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans;
using Orleans.Runtime;
using Vortex.Catalog.Exceptions;
using Vortex.Catalog.Grains;
using Vortex.Primitives.Catalog;
using Vortex.Primitives.Catalog.Enums;
using Vortex.Primitives.Catalog.Snapshots;
using Vortex.Primitives.Commerce;
using Vortex.Primitives.Events;
using Vortex.Primitives.Inventory.Grains;
using Vortex.Primitives.Orleans.Snapshots.Players;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Enums.Wallet;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Players.Wallet;
using Vortex.Primitives.Rooms;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Database.Tests.Catalog;

/// <summary>
/// Gifting used to hand-roll its own debit-then-grant in the packet handler, which skipped the club
/// gate, the HC discount, the credit-spend tracking, the audit events and the refund. These lock the
/// gift path onto the same guarantees as a normal purchase.
/// </summary>
public sealed class CatalogGiftPurchaseTests
{
    private const int BUYER_ID = 11;
    private const int RECEIVER_ID = 22;
    private const int OFFER_ID = 900;

    /// <summary>
    /// Stuff type 0 names no <c>present_gen*</c> furniture, so wrapping cannot be built and the
    /// purchase falls back to granting the offer outright. That is deliberate here: these tests are
    /// about the economy guarantees — club gate, discount, spend tracking, refund — and the fallback
    /// keeps them asserting on the recipient's inventory rather than on the contents of a box.
    /// </summary>
    private static readonly GiftWrappingSpec NoWrapping = new()
    {
        StuffTypeId = 0,
        BoxTypeId = 0,
        RibbonTypeId = 0,
        Message = string.Empty,
        ShowPurchaserName = false,
    };

    [Fact]
    public async Task PurchaseOfferAsGiftAsync_GrantsToReceiverAndChargesBuyer()
    {
        CatalogPurchaseHarness harness = new CatalogPurchaseHarness(
            CatalogOffers.New(OFFER_ID, costCredits: 50),
            BUYER_ID
        );

        CatalogOfferSnapshot offer = await harness
            .Grain.PurchaseOfferAsGiftAsync(
                CatalogType.Normal,
                OFFER_ID,
                string.Empty,
                new PlayerId(RECEIVER_ID),
                NoWrapping,
                CancellationToken.None
            )
            .ConfigureAwait(true);

        offer.Id.Should().Be(OFFER_ID);
        harness.GrantedToPlayerIds.Should().Equal(RECEIVER_ID);
        harness.DebitedPlayerIds.Should().Equal(BUYER_ID);
        harness.DebitRequests.Single().Amount.Should().Be(50);
        harness.TrackedCreditSpend.Should().Equal(50);
        harness.CreditBackCalls.Should().Be(0);

        // Gifting is a purchase that moves between two players, and it used to leave no record of
        // itself at all: it called the overload with no operation id, so nothing was opened, nothing
        // was receipted, and a retry could not tell itself apart from a second gift.
        harness
            .Journal.Opened.Should()
            .ContainSingle()
            .Which.Kind.Should()
            .Be(CommerceOperationKind.Gift);
        harness
            .Journal.Transitions.Should()
            .Contain(t => t.State == CommerceOperationState.Completed);
    }

    [Fact]
    public async Task PurchaseOfferAsGiftAsync_MembershipOnlyOffer_NonMemberIsRefusedBeforeAnyDebit()
    {
        CatalogPurchaseHarness harness = new CatalogPurchaseHarness(
            CatalogOffers.New(OFFER_ID, costCredits: 50, membershipLevel: 1),
            BUYER_ID
        );

        Func<Task> act = () =>
            harness.Grain.PurchaseOfferAsGiftAsync(
                CatalogType.Normal,
                OFFER_ID,
                string.Empty,
                new PlayerId(RECEIVER_ID),
                NoWrapping,
                CancellationToken.None
            );

        CatalogPurchaseException ex = (
            await act.Should().ThrowAsync<CatalogPurchaseException>().ConfigureAwait(true)
        ).Which;

        ex.ErrorType.Should().Be(CatalogPurchaseErrorType.RequiresMembership);
        harness.DebitRequests.Should().BeEmpty();
        harness.GrantedToPlayerIds.Should().BeEmpty();
    }

    [Fact]
    public async Task PurchaseOfferAsGiftAsync_MembershipMember_GetsTheOfferDiscount()
    {
        CatalogPurchaseHarness harness = new CatalogPurchaseHarness(
            CatalogOffers.New(OFFER_ID, costCredits: 100, discountPercent: 25),
            BUYER_ID
        )
        {
            Membership = new MembershipSnapshot { IsActive = true, IsVip = false },
        };

        await harness
            .Grain.PurchaseOfferAsGiftAsync(
                CatalogType.Normal,
                OFFER_ID,
                string.Empty,
                new PlayerId(RECEIVER_ID),
                NoWrapping,
                CancellationToken.None
            )
            .ConfigureAwait(true);

        harness.DebitRequests.Single().Amount.Should().Be(75);
        harness.TrackedCreditSpend.Should().Equal(75);
    }

    [Fact]
    public async Task PurchaseOfferAsGiftAsync_NonGiftableOffer_IsRefused()
    {
        CatalogPurchaseHarness harness = new CatalogPurchaseHarness(
            CatalogOffers.New(OFFER_ID, costCredits: 50, canGift: false),
            BUYER_ID
        );

        Func<Task> act = () =>
            harness.Grain.PurchaseOfferAsGiftAsync(
                CatalogType.Normal,
                OFFER_ID,
                string.Empty,
                new PlayerId(RECEIVER_ID),
                NoWrapping,
                CancellationToken.None
            );

        await act.Should().ThrowAsync<CatalogPurchaseException>().ConfigureAwait(true);
        harness.DebitRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task PurchaseOfferAsGiftAsync_GrantFails_RefundsTheBuyer()
    {
        CatalogPurchaseHarness harness = new CatalogPurchaseHarness(
            CatalogOffers.New(OFFER_ID, costCredits: 50),
            BUYER_ID
        )
        {
            GrantThrows = true,
        };

        Func<Task> act = () =>
            harness.Grain.PurchaseOfferAsGiftAsync(
                CatalogType.Normal,
                OFFER_ID,
                string.Empty,
                new PlayerId(RECEIVER_ID),
                NoWrapping,
                CancellationToken.None
            );

        await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(true);
        harness.CreditBackCalls.Should().Be(1);
    }

    [Fact]
    public async Task PurchaseOfferAsGiftAsync_PublishesSpendAndRecipientEvents()
    {
        CatalogPurchaseHarness harness = new CatalogPurchaseHarness(
            CatalogOffers.New(OFFER_ID, costCredits: 50),
            BUYER_ID
        );

        await harness
            .Grain.PurchaseOfferAsGiftAsync(
                CatalogType.Normal,
                OFFER_ID,
                string.Empty,
                new PlayerId(RECEIVER_ID),
                NoWrapping,
                CancellationToken.None
            )
            .ConfigureAwait(true);

        harness.Events.OfType<CatalogPurchasedEvent>().Single().PlayerId.Should().Be(BUYER_ID);

        CatalogGiftPurchasedEvent gift = harness
            .Events.OfType<CatalogGiftPurchasedEvent>()
            .Single();
        gift.BuyerPlayerId.Should().Be(BUYER_ID);
        gift.ReceiverPlayerId.Should().Be(RECEIVER_ID);
        gift.OfferId.Should().Be(OFFER_ID);
        gift.CreditCost.Should().Be(50);
    }

    [Fact]
    public async Task PurchaseOfferAsGiftAsync_InsufficientBalance_NeverGrants()
    {
        CatalogPurchaseHarness harness = new CatalogPurchaseHarness(
            CatalogOffers.New(OFFER_ID, costCredits: 50),
            BUYER_ID
        )
        {
            DebitSucceeds = false,
        };

        Func<Task> act = () =>
            harness.Grain.PurchaseOfferAsGiftAsync(
                CatalogType.Normal,
                OFFER_ID,
                string.Empty,
                new PlayerId(RECEIVER_ID),
                NoWrapping,
                CancellationToken.None
            );

        CatalogPurchaseException ex = (
            await act.Should().ThrowAsync<CatalogPurchaseException>().ConfigureAwait(true)
        ).Which;

        ex.ErrorType.Should().Be(CatalogPurchaseErrorType.NotEnoughCredits);
        harness.GrantedToPlayerIds.Should().BeEmpty();
    }
}
