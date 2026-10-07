using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Catalog;
using Vortex.Primitives.Catalog.Enums;
using Vortex.Primitives.Catalog.Snapshots;
using Vortex.Primitives.Players.Enums.Wallet;
using Vortex.Primitives.Snapshots.Catalog;
using Xunit;

namespace Vortex.Database.Tests.Catalog;

/// <summary>
/// The plain catalog purchase — the most-travelled money path in the emulator, and the only entry
/// point that takes its quantity straight off the wire (gifting hardcodes one). Every case here is
/// one where getting it wrong means a player is charged for something they did not get, or gets
/// something they were not charged for.
/// </summary>
public sealed class CatalogPurchaseTests
{
    private const int BUYER_ID = 11;
    private const int OFFER_ID = 900;

    private static Task<CatalogOfferSnapshot> BuyAsync(
        CatalogPurchaseHarness harness,
        int quantity
    ) =>
        harness.Grain.PurchaseOfferFromCatalogAsync(
            CatalogType.Normal,
            OFFER_ID,
            string.Empty,
            quantity,
            CancellationToken.None
        );

    /// <summary>
    /// The quantity is wire data with no upper bound of its own, and it drives two separate things:
    /// the price (<c>CostCredits * quantity</c>) and the number of entities the grant allocates. Past
    /// a large enough value the price wraps negative, the debit request is dropped, and the goods are
    /// handed over for free — while the grant allocates a copy per unit. Removing the ceiling check
    /// in <c>PurchaseOfferFromCatalogAsync</c> is the edit this test exists to fail on.
    /// </summary>
    [Fact]
    public async Task AQuantityAboveTheAdvertisedCeiling_IsRefusedBeforeAnyDebitOrGrant()
    {
        CatalogPurchaseHarness harness = new CatalogPurchaseHarness(
            CatalogOffers.New(OFFER_ID, costCredits: 10),
            BUYER_ID
        );

        Func<Task> act = () =>
            BuyAsync(harness, BundleDiscountRulesetSnapshot.DEFAULT_MAX_PURCHASE_SIZE + 1);

        await act.Should().ThrowAsync<CatalogPurchaseException>().ConfigureAwait(true);
        harness.DebitRequests.Should().BeEmpty();
        harness.GrantedQuantities.Should().BeEmpty();
    }

    /// <summary>
    /// The wrap that makes the goods free needs a quantity in the hundreds of millions, so the proof
    /// that the ceiling is what stops it has to be the arithmetic itself, not just a rejected packet.
    /// </summary>
    [Fact]
    public void ThePriceOfAnUnboundedQuantity_WouldWrapNegative()
    {
        const int costCredits = 10;
        const int hostileQuantity = 300_000_000;

        unchecked(costCredits * hostileQuantity).Should().BeNegative();
        ((long)costCredits * hostileQuantity).Should().BeGreaterThan(int.MaxValue);
    }

    /// <summary>The ceiling the server advertises has to be purchasable, or the client's own
    /// quantity selector offers a maximum the server refuses.</summary>
    [Fact]
    public async Task TheAdvertisedCeilingItself_IsAllowed()
    {
        CatalogPurchaseHarness harness = new CatalogPurchaseHarness(
            CatalogOffers.New(OFFER_ID, costCredits: 1),
            BUYER_ID
        );

        await BuyAsync(harness, BundleDiscountRulesetSnapshot.DEFAULT_MAX_PURCHASE_SIZE)
            .ConfigureAwait(true);

        harness
            .GrantedQuantities.Should()
            .Equal(BundleDiscountRulesetSnapshot.DEFAULT_MAX_PURCHASE_SIZE);
    }

    /// <summary>
    /// The charge and the delivery are computed from the same field, so they have to agree. A
    /// purchase that charged for one and delivered many would be free duplication.
    /// </summary>
    [Fact]
    public async Task TheQuantityCharged_IsTheQuantityDelivered()
    {
        CatalogPurchaseHarness harness = new CatalogPurchaseHarness(
            CatalogOffers.New(OFFER_ID, costCredits: 50),
            BUYER_ID
        );

        await BuyAsync(harness, 3).ConfigureAwait(true);

        harness.DebitRequests.Single().Amount.Should().Be(150);
        harness.GrantedQuantities.Should().Equal(3);
        harness.TrackedCreditSpend.Should().Equal(150);
    }

    /// <summary>Every currency the offer charges in scales with the quantity, not just credits.</summary>
    [Fact]
    public async Task EveryCurrencyOnTheOffer_ScalesWithTheQuantity()
    {
        CatalogPurchaseHarness harness = new CatalogPurchaseHarness(
            CatalogOffers.New(
                OFFER_ID,
                costCredits: 10,
                costSilver: 7,
                costCurrency: 3,
                currencyTypeId: 5
            ),
            BUYER_ID
        );

        await BuyAsync(harness, 4).ConfigureAwait(true);

        harness
            .DebitRequests.Single(r => r.CurrencyKind.CurrencyType == CurrencyType.Credits)
            .Amount.Should()
            .Be(40);
        harness
            .DebitRequests.Single(r => r.CurrencyKind.CurrencyType == CurrencyType.Silver)
            .Amount.Should()
            .Be(28);
        harness
            .DebitRequests.Single(r => r.CurrencyKind.CurrencyType == CurrencyType.ActivityPoints)
            .Amount.Should()
            .Be(12);
    }

    /// <summary>
    /// The quantity ceiling makes the wrap unreachable from the wire, but not from the catalogue: an
    /// offer priced past what any allowed quantity can multiply would still wrap, drop its debit and
    /// go out free. A mistyped price has to fail loudly instead.
    /// </summary>
    [Fact]
    public async Task AnOfferPricedBeyondWhatTheQuantityCanMultiply_IsRefusedAsMisconfigured()
    {
        CatalogPurchaseHarness harness = new CatalogPurchaseHarness(
            CatalogOffers.New(OFFER_ID, costCredits: int.MaxValue / 2),
            BUYER_ID
        );

        Func<Task> act = () => BuyAsync(harness, 3);

        CatalogPurchaseException ex = (
            await act.Should().ThrowAsync<CatalogPurchaseException>().ConfigureAwait(true)
        ).Which;

        ex.ErrorType.Should().Be(CatalogPurchaseErrorType.OfferMisconfigured);
        harness.DebitRequests.Should().BeEmpty();
        harness.GrantedQuantities.Should().BeEmpty();
    }

    /// <summary>A quantity of zero or below is the client's problem, not a free-items bug: it buys
    /// exactly one.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public async Task ANonPositiveQuantity_BuysExactlyOne(int quantity)
    {
        CatalogPurchaseHarness harness = new CatalogPurchaseHarness(
            CatalogOffers.New(OFFER_ID, costCredits: 50),
            BUYER_ID
        );

        await BuyAsync(harness, quantity).ConfigureAwait(true);

        harness.DebitRequests.Single().Amount.Should().Be(50);
        harness.GrantedQuantities.Should().Equal(1);
    }

    /// <summary>The buyer funds the purchase and the buyer receives it — this path has no
    /// recipient.</summary>
    [Fact]
    public async Task TheOffer_IsGrantedToTheBuyer()
    {
        CatalogPurchaseHarness harness = new CatalogPurchaseHarness(
            CatalogOffers.New(OFFER_ID, costCredits: 50),
            BUYER_ID
        );

        await BuyAsync(harness, 1).ConfigureAwait(true);

        harness.GrantedToPlayerIds.Should().Equal(BUYER_ID);
        harness.DebitedPlayerIds.Should().Equal(BUYER_ID);
    }

    [Fact]
    public async Task InsufficientBalance_NeverGrants()
    {
        CatalogPurchaseHarness harness = new CatalogPurchaseHarness(
            CatalogOffers.New(OFFER_ID, costCredits: 50),
            BUYER_ID
        )
        {
            DebitSucceeds = false,
        };

        Func<Task> act = () => BuyAsync(harness, 1);

        CatalogPurchaseException ex = (
            await act.Should().ThrowAsync<CatalogPurchaseException>().ConfigureAwait(true)
        ).Which;

        ex.ErrorType.Should().Be(CatalogPurchaseErrorType.NotEnoughCredits);
        harness.GrantedQuantities.Should().BeEmpty();
    }

    /// <summary>The shared purchase primitive owns the refund; this pins that the catalog path
    /// actually routes through it rather than debiting on its own.</summary>
    [Fact]
    public async Task AGrantThatFails_RefundsTheBuyer()
    {
        CatalogPurchaseHarness harness = new CatalogPurchaseHarness(
            CatalogOffers.New(OFFER_ID, costCredits: 50),
            BUYER_ID
        )
        {
            GrantThrows = true,
        };

        Func<Task> act = () => BuyAsync(harness, 1);

        await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(true);
        harness.CreditBackCalls.Should().Be(1);
    }
}
