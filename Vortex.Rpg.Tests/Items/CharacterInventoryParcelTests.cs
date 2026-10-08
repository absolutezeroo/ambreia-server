using FluentAssertions;
using Vortex.Primitives.Rpg.Items;
using Vortex.Primitives.Rpg.Items.Enums;
using Vortex.Rpg.Items;
using Xunit;

namespace Vortex.Rpg.Tests.Items;

/// <summary>Spec Objets §7: a parcel goes into the bag whole, or not at all.</summary>
public sealed class CharacterInventoryParcelTests
{
    [Fact]
    public void AClaim_GoesIntoStacksThenAnEmptyCase()
    {
        CharacterInventory inventory = TestItems.Inventory(
            TestItems.InBag(TestItems.Shard, 0, 45),
            TestItems.At(TestItems.Shard, ItemContainer.Parcel, 0, 20)
        );

        inventory.ClaimParcel(0).Should().Be(ItemOutcome.Ok);

        inventory.At(ItemContainer.Bag, 0)!.Quantity.Should().Be(50);
        inventory.At(ItemContainer.Bag, 1)!.Quantity.Should().Be(15);
        inventory.At(ItemContainer.Parcel, 0).Should().BeNull();
    }

    [Fact]
    public void AClaim_ThatFitsInAStack_LeavesNoParcelBehind()
    {
        CharacterInventory inventory = TestItems.Inventory(
            TestItems.InBag(TestItems.Shard, 0, 10),
            TestItems.At(TestItems.Shard, ItemContainer.Parcel, 0, 20)
        );

        inventory.ClaimParcel(0).Should().Be(ItemOutcome.Ok);

        inventory.At(ItemContainer.Bag, 0)!.Quantity.Should().Be(30);
        inventory.Live.Should().ContainSingle();
    }

    [Fact]
    public void AClaim_WithAFullBag_IsBagFullAndChangesNothing()
    {
        CharacterInventory inventory = TestItems.Inventory(
            1,
            50,
            TestItems.InBag(TestItems.Sword, 0),
            TestItems.At(TestItems.Shard, ItemContainer.Parcel, 0, 20)
        );

        inventory.ClaimParcel(0).Should().Be(ItemOutcome.BagFull);

        inventory.At(ItemContainer.Parcel, 0)!.Quantity.Should().Be(20);
        inventory.Dirty.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void AnUnknownParcel_IsNotFound(int parcel)
    {
        CharacterInventory inventory = TestItems.Inventory();

        inventory.ClaimParcel(parcel).Should().Be(ItemOutcome.NotFound);
    }
}
