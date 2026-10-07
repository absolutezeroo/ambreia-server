using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Vortex.Inventory.Fulfillment;
using Vortex.Inventory.Fulfillment.Strategies;
using Vortex.Logging;
using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Furniture.Snapshots;
using Vortex.Primitives.Furniture.StuffData;
using Vortex.Primitives.Groups.Snapshots;
using Xunit;

namespace Vortex.Database.Tests.Commerce;

/// <summary>
/// What an offer promises, worked out before anything durable happens.
/// </summary>
/// <remarks>
/// Every rule here used to live inside a 280-line method that could only be exercised by performing
/// a purchase against a database, and every one of them has been a bug: bundles collapsing to one of
/// each item, guild layouts stamped onto furni that cannot carry them, bots granted invisible. The
/// planner is pure, so they are ordinary unit tests now.
/// </remarks>
public sealed class CatalogFulfillmentPlannerTests
{
    private const int FLOOR_ID = InventoryGrainFixture.FLOOR_DEFINITION_ID;
    private const int GUILD_FURNI_ID = 60;

    /// <summary>
    /// A bundle is one offer holding several products, and any product may itself bundle more than
    /// one copy. Ignoring the product's own count collapsed every bundle to one of each item.
    /// </summary>
    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(1, 3, 3)]
    [InlineData(2, 3, 6)]
    [InlineData(4, 1, 4)]
    public void TheCopiesGranted_AreQuantityTimesTheProductCount(
        int quantity,
        int productQuantity,
        int expected
    )
    {
        FulfillmentPlan plan = Plan(
            quantity,
            CatalogOffers.Product(1, ProductType.Floor, quantity: productQuantity)
        );

        plan.Furniture.Should().HaveCount(expected);
        plan.Furniture.Should().OnlyContain(f => f.DefinitionId == FLOOR_ID);
    }

    /// <summary>A quantity of zero or less is one purchase, not none and not a negative one.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void ANonPositiveQuantity_PlansExactlyOne(int quantity)
    {
        Plan(quantity, CatalogOffers.Product(1, ProductType.Floor))
            .Furniture.Should()
            .ContainSingle();
    }

    /// <summary>
    /// Only string-array furni can carry the guild layout. Stamping it onto anything else corrupts
    /// the item's own stuff data — a dice that comes back as a badge.
    /// </summary>
    [Fact]
    public void TheGuildLayout_IsStampedOnlyOntoFurniThatCanCarryIt()
    {
        FulfillmentPlan plan = Plan(
            quantity: 1,
            guild: Guild(),
            products:
            [
                CatalogOffers.Product(1, ProductType.Floor, definitionId: FLOOR_ID),
                CatalogOffers.Product(2, ProductType.Floor, definitionId: GUILD_FURNI_ID),
            ]
        );

        plan.Furniture.Should().HaveCount(2);

        plan.Furniture.Single(f => f.DefinitionId == FLOOR_ID)
            .ExtraData.Should()
            .BeNull("a legacy-key item cannot hold the guild layout");

        plan.Furniture.Single(f => f.DefinitionId == GUILD_FURNI_ID)
            .ExtraData.Should()
            .NotBeNullOrEmpty("a string-key item is exactly what the layout goes into");
    }

    [Fact]
    public void WithNoGuild_NothingIsStamped()
    {
        FulfillmentPlan plan = Plan(
            quantity: 1,
            CatalogOffers.Product(1, ProductType.Floor, definitionId: GUILD_FURNI_ID)
        );

        plan.Furniture.Should().OnlyContain(f => f.ExtraData == null);
    }

    /// <summary>A product naming a definition nobody has is a deterministic error, and it belongs
    /// before the debit rather than halfway through the grant.</summary>
    [Fact]
    public void AnUnknownDefinition_FailsThePlanRatherThanThePurchase()
    {
        Action plan = () =>
            Plan(quantity: 1, CatalogOffers.Product(1, ProductType.Floor, definitionId: 9999));

        plan.Should().Throw<VortexException>();
    }

    [Theory]
    [InlineData("42", 42, 0, 0)]
    [InlineData("42:3600", 42, 0, 3600)]
    [InlineData("42:3600:7", 42, 7, 3600)]
    public void AnEffectProduct_IsReadFromItsColonSeparatedParam(
        string extraParam,
        int effectId,
        int subType,
        int duration
    )
    {
        FulfillmentPlan plan = Plan(
            quantity: 1,
            CatalogOffers.Product(1, ProductType.Effect, extraParam: extraParam)
        );

        plan.Effects.Should().ContainSingle();
        plan.Effects[0].Should().Be(new PlannedEffect(effectId, subType, duration));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-number")]
    [InlineData("0")]
    [InlineData("-1")]
    public void AnEffectProductWithNoUsableId_PlansNothing(string extraParam)
    {
        Plan(quantity: 1, CatalogOffers.Product(1, ProductType.Effect, extraParam: extraParam))
            .Effects.Should()
            .BeEmpty();
    }

    /// <summary>Effects follow the same multiplier as furniture: one grant per copy.</summary>
    [Fact]
    public void EffectsAreGrantedOncePerCopy()
    {
        FulfillmentPlan plan = Plan(
            quantity: 2,
            CatalogOffers.Product(1, ProductType.Effect, quantity: 3, extraParam: "42")
        );

        plan.Effects.Should().HaveCount(6);
    }

    /// <summary>
    /// A bot with no figure is skipped rather than granted. An invisible bot in the inventory is
    /// worse than a purchase that visibly delivered nothing.
    /// </summary>
    [Fact]
    public void ABotWithNoFigure_IsSkipped()
    {
        Plan(
            quantity: 1,
            CatalogOffers.Product(1, ProductType.Robot, extraParam: "name:Robbie;gender:m")
        )
            .Bots.Should()
            .BeEmpty();
    }

    [Fact]
    public void ABotProduct_CarriesItsFigureAndName()
    {
        FulfillmentPlan plan = Plan(
            quantity: 1,
            CatalogOffers.Product(
                1,
                ProductType.Robot,
                extraParam: "name:Robbie;figure:hd-180-1;gender:m"
            )
        );

        plan.Bots.Should().ContainSingle();
        plan.Bots[0].Figure.Should().Be("hd-180-1");
        plan.Bots[0].Name.Should().Be("Robbie");
    }

    /// <summary>The pet's name, race and colour come from what the player typed, not from the offer.</summary>
    [Fact]
    public void APetProduct_TakesItsNameRaceAndColourFromThePurchase()
    {
        FulfillmentPlan plan = Plan(
            quantity: 1,
            extraParam: "Rex\n3\nff8800",
            guild: null,
            products: [CatalogOffers.Product(1, ProductType.Pet, extraParam: "1")]
        );

        plan.Pets.Should().ContainSingle();
        plan.Pets[0].Name.Should().Be("Rex");
        plan.Pets[0].Race.Should().Be(3);
        plan.Pets[0].Color.Should().Be("ff8800");
        plan.Pets[0].Type.Should().Be(1);
    }

    [Fact]
    public void APetWithNoName_IsCalledPetRatherThanNothing()
    {
        FulfillmentPlan plan = Plan(
            quantity: 1,
            extraParam: "   \n1",
            guild: null,
            products: [CatalogOffers.Product(1, ProductType.Pet, extraParam: "1")]
        );

        plan.Pets[0].Name.Should().Be("Pet");
    }

    [Fact]
    public void ABadgeProduct_PlansItsCode()
    {
        Plan(quantity: 1, CatalogOffers.Product(1, ProductType.Badge, extraParam: "ACH_TEST1"))
            .BadgeCodes.Should()
            .Equal(["ACH_TEST1"]);
    }

    /// <summary>A badge is owned or not; buying five of an offer does not plan five of it.</summary>
    [Fact]
    public void ABadge_IsPlannedOnceWhateverTheQuantity()
    {
        Plan(quantity: 5, CatalogOffers.Product(1, ProductType.Badge, extraParam: "ACH_TEST1"))
            .BadgeCodes.Should()
            .ContainSingle();
    }

    [Fact]
    public void AnOfferOfProductTypesNothingCanGrant_PlansNothing()
    {
        Plan(quantity: 1, CatalogOffers.Product(1, ProductType.Badge, extraParam: "   "))
            .IsEmpty.Should()
            .BeTrue();
    }

    /// <summary>
    /// A product type nothing answers for promises nothing, rather than refusing the purchase. A
    /// catalogue carries rows for features an emulator may not have yet, and a bundle containing one
    /// of them should still grant the rest.
    /// </summary>
    [Fact]
    public void AProductTypeNoStrategyClaims_ContributesNothing()
    {
        CatalogFulfillmentPlanner planner = new([new BadgeFulfillmentStrategy()]);

        FulfillmentPlan plan = planner.Plan(
            CatalogOffers.Offer(
                1,
                10,
                CatalogOffers.Product(1, ProductType.Floor, definitionId: 1),
                CatalogOffers.Product(2, ProductType.Badge, extraParam: "ACH_Test1")
            ),
            string.Empty,
            1,
            null
        );

        plan.BadgeCodes.Should().Equal(["ACH_Test1"]);
        plan.Furniture.Should().BeEmpty("no strategy answers for floor furniture in this planner");
        plan.IsEmpty.Should().BeFalse();
    }

    /// <summary>
    /// A later strategy replaces an earlier one for the same product type. That is the whole point of
    /// the seam: a hotel with its own rule for a type does not have to filter the built-in out first.
    /// </summary>
    [Fact]
    public void AStrategyRegisteredLast_ReplacesTheOneBeforeIt()
    {
        CatalogFulfillmentPlanner planner = new([
            new BadgeFulfillmentStrategy(),
            new AlwaysOneFixedBadge(),
        ]);

        FulfillmentPlan plan = planner.Plan(
            CatalogOffers.Offer(
                1,
                10,
                CatalogOffers.Product(2, ProductType.Badge, extraParam: "ACH_Test1")
            ),
            string.Empty,
            1,
            null
        );

        plan.BadgeCodes.Should().Equal(["OVERRIDDEN"]);
    }

    private sealed class AlwaysOneFixedBadge : IFulfillmentStrategy
    {
        public IReadOnlyList<ProductType> Handles { get; } = [ProductType.Badge];

        public void Contribute(in FulfillmentRequest request, FulfillmentPlanBuilder into) =>
            into.AddBadge("OVERRIDDEN");
    }

    private static FulfillmentPlan Plan(
        int quantity,
        params Vortex.Primitives.Catalog.Snapshots.CatalogProductSnapshot[] products
    ) => Plan(quantity, string.Empty, null, products);

    private static FulfillmentPlan Plan(
        int quantity,
        GuildFurniIdentitySnapshot? guild,
        params Vortex.Primitives.Catalog.Snapshots.CatalogProductSnapshot[] products
    ) => Plan(quantity, string.Empty, guild, products);

    private static FulfillmentPlan Plan(
        int quantity,
        string extraParam,
        GuildFurniIdentitySnapshot? guild,
        params Vortex.Primitives.Catalog.Snapshots.CatalogProductSnapshot[] products
    ) =>
        new CatalogFulfillmentPlanner(
            new PlannerDefinitions(),
            NullLogger<CatalogFulfillmentPlanner>.Instance
        ).Plan(CatalogOffers.Offer(1, 10, products), extraParam, quantity, guild);

    private static GuildFurniIdentitySnapshot Guild() =>
        new()
        {
            GroupId = 7,
            BadgeCode = "b12345",
            ColorOneHex = "ff0000",
            ColorTwoHex = "00ff00",
        };

    /// <summary>Two definitions: one that can carry the guild layout and one that cannot.</summary>
    private sealed class PlannerDefinitions : IFurnitureDefinitionProvider
    {
        public FurnitureDefinitionSnapshot? TryGetDefinition(int id) =>
            id switch
            {
                FLOOR_ID => InventoryGrainFixture.Definition(FLOOR_ID),
                GUILD_FURNI_ID => InventoryGrainFixture.Definition(GUILD_FURNI_ID) with
                {
                    StuffDataType = StuffDataType.StringKey,
                },
                _ => null,
            };

        public FurnitureDefinitionSnapshot? TryGetDefinitionByName(string name) => null;

        public Task ReloadAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
