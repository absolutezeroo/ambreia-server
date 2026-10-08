using System.Linq;
using FluentAssertions;
using Vortex.Primitives.Rpg.Items;
using Vortex.Primitives.Rpg.Items.Enums;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Items;
using Xunit;

namespace Vortex.Rpg.Tests.Items;

/// <summary>Spec Objets §7: a grant fills stacks, then empty cases, then parcels; past the parcels,
/// nothing at all.</summary>
public sealed class CharacterInventoryGrantTests
{
    [Fact]
    public void AGrant_FillsStacksThenEmptyCases()
    {
        CharacterInventory inventory = TestItems.Inventory(TestItems.InBag(TestItems.Shard, 0, 45));

        GrantPlan plan = inventory.Grant([Line(TestItems.Shard, 10)]);

        (plan.Outcome, plan.ToBag, plan.ToParcels).Should().Be((ItemOutcome.Ok, 10, 0));
        inventory.At(ItemContainer.Bag, 0)!.Quantity.Should().Be(50);
        inventory.At(ItemContainer.Bag, 1)!.Quantity.Should().Be(5);
        plan.Placed.Select(p => p.Quantity).Should().Equal(5, 5);
    }

    [Fact]
    public void AFullBag_SendsTheRestToParcels()
    {
        CharacterInventory inventory = TestItems.Inventory(
            1,
            50,
            TestItems.InBag(TestItems.Sword, 0)
        );

        GrantPlan plan = inventory.Grant([Line(TestItems.Shard, 20)]);

        (plan.Outcome, plan.ToBag, plan.ToParcels).Should().Be((ItemOutcome.Ok, 0, 20));
        inventory.At(ItemContainer.Parcel, 0)!.Quantity.Should().Be(20);
    }

    [Fact]
    public void FullParcels_GrantNothing()
    {
        CharacterInventory inventory = TestItems.Inventory(
            1,
            1,
            TestItems.InBag(TestItems.Sword, 0),
            TestItems.At(TestItems.Shard, ItemContainer.Parcel, 0, 50)
        );

        GrantPlan plan = inventory.Grant([Line(TestItems.Shard, 1)]);

        plan.Outcome.Should().Be(ItemOutcome.ParcelsFull);
        inventory.Live.Should().HaveCount(2);
        inventory.Dirty.Should().BeEmpty();
    }

    [Fact]
    public void AGrantOfSeveralLines_IsWholeOrNothing()
    {
        CharacterInventory inventory = TestItems.Inventory();

        GrantPlan plan = inventory.Grant([
            Line(TestItems.Sword, 1),
            new ItemGrantLine { DefinitionId = "item.nope", Quantity = 1 },
        ]);

        plan.Outcome.Should().Be(ItemOutcome.UnknownItem);
        inventory.Live.Should().BeEmpty();
    }

    [Fact]
    public void AGrantThatOverflowsTheParcelsMidway_LeavesTheBagAsItWas()
    {
        CharacterInventory inventory = TestItems.Inventory(
            1,
            1,
            TestItems.InBag(TestItems.Shard, 0, 40)
        );

        // 10 fill the stack, 50 make a parcel, the last 10 find no room.
        GrantPlan plan = inventory.Grant([Line(TestItems.Shard, 70)]);

        plan.Outcome.Should().Be(ItemOutcome.ParcelsFull);
        inventory.At(ItemContainer.Bag, 0)!.Quantity.Should().Be(40);
        inventory.Live.Should().ContainSingle();
        inventory.Dirty.Should().BeEmpty();
    }

    [Theory]
    [InlineData("item.nope")]
    [InlineData("item.material.old_shard")]
    public void AnUnknownOrRetiredItem_IsRefused(string definitionId)
    {
        CharacterInventory inventory = TestItems.Inventory();

        inventory
            .Grant([new ItemGrantLine { DefinitionId = definitionId, Quantity = 1 }])
            .Outcome.Should()
            .Be(ItemOutcome.UnknownItem);
        inventory.Live.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void AQuantityOutOfBounds_IsRefused(int quantity)
    {
        CharacterInventory inventory = TestItems.Inventory();

        inventory
            .Grant([Line(TestItems.Shard, quantity)])
            .Outcome.Should()
            .Be(ItemOutcome.InvalidQuantity);
        inventory.Grant([]).Outcome.Should().Be(ItemOutcome.InvalidQuantity);
        inventory.Live.Should().BeEmpty();
    }

    [Fact]
    public void APickupBindingItem_IsBoundAtGrant()
    {
        CharacterInventory inventory = TestItems.Inventory();

        inventory.Grant([Line(TestItems.Letter, 1)]);

        inventory.At(ItemContainer.Bag, 0)!.Bound.Should().BeTrue();
    }

    [Fact]
    public void ABoundAndAFreeCopy_DoNotStack()
    {
        ItemInstance bound = TestItems.InBag(TestItems.Shard, 0, 10);
        bound.Bound = true;
        CharacterInventory inventory = TestItems.Inventory(bound);

        inventory.Grant([Line(TestItems.Shard, 5)]);

        inventory.At(ItemContainer.Bag, 0)!.Quantity.Should().Be(10);
        inventory.At(ItemContainer.Bag, 1)!.Quantity.Should().Be(5);
    }

    [Fact]
    public void AnEquipment_StartsAtFullDurability()
    {
        CharacterInventory inventory = TestItems.Inventory();

        inventory.Grant([Line(TestItems.Sword, 1), Line(TestItems.Shard, 1)]);

        inventory.At(ItemContainer.Bag, 0)!.Durability.Should().Be(50);
        inventory.At(ItemContainer.Bag, 1)!.Durability.Should().BeNull();
    }

    private static ItemGrantLine Line(ItemDefinition definition, int quantity) =>
        new() { DefinitionId = definition.Id, Quantity = quantity };
}
