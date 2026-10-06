using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Catalog.Snapshots;
using Xunit;

namespace Vortex.Database.Tests.Catalog;

/// <summary>
/// Opening a present, the catalogue entry point that pays for nothing because it was paid for when
/// the present was bought.
/// </summary>
public sealed class CatalogPresentTests
{
    private const int BUYER_ID = 11;
    private const int OFFER_ID = 900;

    /// <summary>
    /// Opening a present is the one catalogue path that grants without charging: the credits were
    /// taken when the present was bought. So the invariant is not about money, it is that the
    /// contents come out exactly once and that nothing is debited on the way.
    /// </summary>
    [Fact]
    public async Task OpeningAPresent_GrantsTheContentsAndChargesNothing()
    {
        CatalogPurchaseHarness harness = new(CatalogOffers.New(OFFER_ID, 50), BUYER_ID);

        CatalogOfferSnapshot? offer = await harness
            .Grain.GrantPresentContentsAsync(OFFER_ID, string.Empty, CancellationToken.None)
            .ConfigureAwait(true);

        offer!.Id.Should().Be(OFFER_ID);
        harness.GrantedToPlayerIds.Should().Equal(BUYER_ID);
        harness.GrantedQuantities.Should().Equal(1);
        harness.DebitRequests.Should().BeEmpty();
        harness.DebitedPlayerIds.Should().BeEmpty();
    }

    /// <summary>
    /// A present outlives the catalogue page it came from, so an offer that has since been removed
    /// is an ordinary outcome. It has to hand back nothing rather than throw: the player is holding
    /// a box, and an exception here would leave them holding it forever.
    /// </summary>
    [Fact]
    public async Task APresentWhoseOfferIsGone_GrantsNothingAndDoesNotThrow()
    {
        CatalogPurchaseHarness harness = new(CatalogOffers.New(OFFER_ID, 50), BUYER_ID);

        CatalogOfferSnapshot? offer = await harness
            .Grain.GrantPresentContentsAsync(OFFER_ID + 1, string.Empty, CancellationToken.None)
            .ConfigureAwait(true);

        offer.Should().BeNull();
        harness.GrantedQuantities.Should().BeEmpty();
    }
}
