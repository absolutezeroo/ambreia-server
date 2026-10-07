using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Catalog;
using Vortex.Primitives.Catalog.Enums;
using Vortex.Primitives.Events;
using Xunit;

namespace Vortex.Database.Tests.Catalog;

/// <summary>
/// The purchase gate an outside behaviour can hold. It sits after the price is computed and before
/// the wallet or the commerce journal is touched, which is the last point where refusing costs
/// nothing — a veto after the debit would need a refund, and a veto after the grant could not be
/// honoured at all.
/// </summary>
public sealed class CatalogPurchaseVetoTests
{
    private const int BUYER_ID = 11;
    private const int OFFER_ID = 900;

    [Fact]
    public async Task ThePurchaseIsAnnouncedWithItsPrice_BeforeAnythingDurableHappens()
    {
        CatalogPurchaseHarness harness = new(
            CatalogOffers.New(OFFER_ID, costCredits: 10),
            BUYER_ID
        );

        await BuyAsync(harness, quantity: 3).ConfigureAwait(true);

        CatalogPurchasingEvent purchasing = harness
            .Events.OfType<CatalogPurchasingEvent>()
            .Should()
            .ContainSingle()
            .Subject;

        purchasing.PlayerId.Should().Be(BUYER_ID);
        purchasing.OfferId.Should().Be(OFFER_ID);
        purchasing.Quantity.Should().Be(3);
        purchasing.CreditCost.Should().Be(30);
    }

    [Fact]
    public async Task CancellingTheEvent_RefusesThePurchase_WithNoDebitAndNoGrant()
    {
        CatalogPurchaseHarness harness = new(CatalogOffers.New(OFFER_ID, costCredits: 10), BUYER_ID)
        {
            CancelPurchase = true,
        };

        Func<Task> act = () => BuyAsync(harness, quantity: 1);

        await act.Should().ThrowAsync<CatalogPurchaseException>().ConfigureAwait(true);

        harness.DebitRequests.Should().BeEmpty();
        harness.GrantedQuantities.Should().BeEmpty();

        // And no completion event either: a cancelled purchase that still announced itself would
        // give quest progress and daily tasks credit for a purchase that never happened.
        harness.Events.OfType<CatalogPurchasedEvent>().Should().BeEmpty();
    }

    private static Task BuyAsync(CatalogPurchaseHarness harness, int quantity) =>
        harness.Grain.PurchaseOfferFromCatalogAsync(
            CatalogType.Normal,
            OFFER_ID,
            string.Empty,
            quantity,
            CancellationToken.None
        );
}
