using System;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Catalog.Snapshots;
using Vortex.Primitives.Furniture.Enums;
using Xunit;

namespace Vortex.Database.Tests.Commerce;

/// <summary>
/// What a catalog grant leaves behind when one of its later steps fails. The grant is not one
/// commit: furniture and badges land together, pets in a commit of their own, bots in another, and
/// effects in a different grain entirely. The wallet's shared purchase primitive refunds the whole
/// price if any of them throws — an invariant written for a grant that was atomic, and kept as the
/// grant stopped being atomic one product family at a time.
/// <para>
/// These are characterisation tests: they record what the code does today, window by window, so the
/// fix has something to change rather than something to argue with. Each one names the PR that
/// flips its assertion.
/// </para>
/// </summary>
public sealed class CatalogGrantWindowTests
{
    /// <summary>An offer with a chair, a badge, a pet and an effect — one product per commit
    /// boundary, which is what makes the windows visible.</summary>
    private static CatalogProductSnapshot[] MultiFamilyOffer() =>
        [
            CatalogOffers.Product(1, ProductType.Floor),
            CatalogOffers.Product(
                2,
                ProductType.Badge,
                extraParam: CommerceFaultHarness.BADGE_CODE
            ),
            CatalogOffers.Product(3, ProductType.Pet, extraParam: "1"),
            CatalogOffers.Product(
                4,
                ProductType.Effect,
                extraParam: $"{CommerceFaultHarness.EFFECT_ID}:0:0"
            ),
        ];

    [Fact]
    public async Task ACompleteGrant_DeliversEveryFamilyAndChargesOnce()
    {
        using CommerceFaultHarness harness = new(MultiFamilyOffer());

        await harness.BuyAsync(extraParam: "Rex");

        (await harness.FurnitureRowsAsync()).Should().Be(1);
        (await harness.BadgeRowsAsync()).Should().Be(1);
        (await harness.PetRowsAsync()).Should().Be(1);
        harness.EffectsGranted.Should().ContainSingle();
        harness.Debits.Should().ContainSingle();
        harness.Refunds.Should().BeEmpty();
    }

    /// <summary>
    /// WINDOW A1, closed. The effect grant is the last step of the purchase and lives in another
    /// grain. It used to throw straight through to the wallet's shared purchase primitive, which
    /// refunded the whole price while the furniture, the badge and the pet stayed in the player's
    /// inventory — a state the code documented without seeing, in a comment on the effect block
    /// saying a throw there "auto-refunds".
    /// </summary>
    /// <remarks>
    /// The local families now commit together, and that commit is the pivot. Past it a failure is
    /// logged and the purchase stands: the player keeps what they paid for, and the effect that did
    /// not land is a known, recorded gap rather than a reason to reverse a delivered sale.
    /// </remarks>
    [Fact]
    public async Task AnEffectThatFailsLast_KeepsTheGoodsAndTheCharge()
    {
        using CommerceFaultHarness harness = new(MultiFamilyOffer())
        {
            Fails = CommerceFaultStep.EffectGrant,
        };

        await harness.BuyAsync(extraParam: "Rex");

        (await harness.FurnitureRowsAsync()).Should().Be(1);
        (await harness.BadgeRowsAsync()).Should().Be(1);
        (await harness.PetRowsAsync()).Should().Be(1);

        harness
            .Refunds.Should()
            .BeEmpty("there is no refund past the pivot; the goods are already the player's");

        harness.EffectsGranted.Should().BeEmpty("that step is the one that failed");
    }

    /// <summary>
    /// WINDOW A1b, closed. The pet's presence notification runs after its row is committed, so it
    /// used to be one more way to end up with goods and a refund.
    /// </summary>
    [Fact]
    public async Task APetNotificationThatFails_KeepsThePetAndTheCharge()
    {
        using CommerceFaultHarness harness = new(MultiFamilyOffer())
        {
            Fails = CommerceFaultStep.PetNotification,
        };

        await harness.BuyAsync(extraParam: "Rex");

        (await harness.FurnitureRowsAsync()).Should().Be(1);
        (await harness.BadgeRowsAsync()).Should().Be(1);
        (await harness.PetRowsAsync()).Should().Be(1);
        harness.Refunds.Should().BeEmpty();
    }

    /// <summary>
    /// The point of the consolidation, stated directly: whatever fails after the local grant, the
    /// player never ends up holding the goods and their money back. That state was reachable from
    /// three different steps before.
    /// </summary>
    [Theory]
    [InlineData(CommerceFaultStep.PetNotification)]
    [InlineData(CommerceFaultStep.BotNotification)]
    [InlineData(CommerceFaultStep.EffectGrant)]
    public async Task NoFailurePastThePivot_ProducesGoodsAndARefund(CommerceFaultStep step)
    {
        using CommerceFaultHarness harness = new([
            CatalogOffers.Product(1, ProductType.Floor),
            CatalogOffers.Product(2, ProductType.Pet, extraParam: "1"),
            CatalogOffers.Product(
                3,
                ProductType.Robot,
                extraParam: "name:Robbie;figure:hd-180-1;gender:m"
            ),
            CatalogOffers.Product(
                4,
                ProductType.Effect,
                extraParam: $"{CommerceFaultHarness.EFFECT_ID}:0:0"
            ),
        ])
        {
            Fails = step,
        };

        await harness.BuyAsync(extraParam: "Rex");

        (await harness.FurnitureRowsAsync()).Should().Be(1);
        harness.Refunds.Should().BeEmpty();
    }

    /// <summary>
    /// A bundle is one offer holding several products, and any product may carry more than one copy.
    /// The copies granted are the purchase quantity times the product's own count — the arithmetic
    /// that collapsed every bundle to one of each item when it was missed once already, and the one
    /// the single-commit consolidation must not disturb.
    /// </summary>
    [Fact]
    public async Task ABundleGrantsQuantityTimesProductCount()
    {
        using CommerceFaultHarness harness = new([
            CatalogOffers.Product(1, ProductType.Floor, quantity: 3),
        ]);

        await harness.BuyAsync(quantity: 2);

        (await harness.FurnitureRowsAsync()).Should().Be(6);
    }

    /// <summary>
    /// A badge the player already owns is skipped rather than duplicated. The guard runs inside the
    /// same commit as the furniture, so the consolidation has to carry it across unchanged.
    /// </summary>
    [Fact]
    public async Task ABadgeAlreadyOwned_IsNotGrantedTwice()
    {
        using CommerceFaultHarness harness = new([
            CatalogOffers.Product(
                1,
                ProductType.Badge,
                extraParam: CommerceFaultHarness.BADGE_CODE
            ),
        ]);

        await harness.BuyAsync();
        await harness.BuyAsync();

        (await harness.BadgeRowsAsync()).Should().Be(1);
    }
}
