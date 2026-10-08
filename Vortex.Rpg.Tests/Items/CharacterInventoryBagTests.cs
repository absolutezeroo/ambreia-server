using System.Linq;
using FluentAssertions;
using Vortex.Primitives.Rpg.Items;
using Vortex.Primitives.Rpg.Items.Enums;
using Vortex.Rpg.Items;
using Xunit;

namespace Vortex.Rpg.Tests.Items;

/// <summary>Spec Objets §7: moving, splitting, destroying and removing in the bag. A refusal
/// changes nothing.</summary>
public sealed class CharacterInventoryBagTests
{
    [Fact]
    public void AMove_ToAnEmptyCase_MovesAndMarksDirty()
    {
        CharacterInventory inventory = TestItems.Inventory(TestItems.InBag(TestItems.Sword, 0));

        inventory.Move(0, 7).Should().Be(ItemOutcome.Ok);

        inventory.At(ItemContainer.Bag, 7)!.DefinitionId.Should().Be(TestItems.Sword.Id);
        inventory.At(ItemContainer.Bag, 0).Should().BeNull();
        inventory.Dirty.Should().ContainSingle();
    }

    [Fact]
    public void AMove_OntoAnotherItem_Swaps()
    {
        CharacterInventory inventory = TestItems.Inventory(
            TestItems.InBag(TestItems.Sword, 0),
            TestItems.InBag(TestItems.Ring, 1)
        );

        inventory.Move(0, 1).Should().Be(ItemOutcome.Ok);

        inventory.At(ItemContainer.Bag, 0)!.DefinitionId.Should().Be(TestItems.Ring.Id);
        inventory.At(ItemContainer.Bag, 1)!.DefinitionId.Should().Be(TestItems.Sword.Id);
    }

    [Fact]
    public void AMove_OntoTheSameStack_MergesUpToTheMaxAndLeavesTheRest()
    {
        CharacterInventory inventory = TestItems.Inventory(
            TestItems.InBag(TestItems.Shard, 0, 30),
            TestItems.InBag(TestItems.Shard, 1, 40)
        );

        inventory.Move(0, 1).Should().Be(ItemOutcome.Ok);

        inventory.At(ItemContainer.Bag, 1)!.Quantity.Should().Be(50);
        inventory.At(ItemContainer.Bag, 0)!.Quantity.Should().Be(20);
    }

    [Fact]
    public void AMove_OfAWholeStackIntoAnother_DeletesTheSource()
    {
        CharacterInventory inventory = TestItems.Inventory(
            TestItems.InBag(TestItems.Shard, 0, 10),
            TestItems.InBag(TestItems.Shard, 1, 10)
        );

        inventory.Move(0, 1).Should().Be(ItemOutcome.Ok);

        inventory.At(ItemContainer.Bag, 1)!.Quantity.Should().Be(20);
        inventory.At(ItemContainer.Bag, 0).Should().BeNull();
        inventory.Dirty.Should().Contain(i => i.Deleted && i.Slot == 0);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 30)]
    [InlineData(0, int.MaxValue)]
    [InlineData(int.MinValue, 1)]
    [InlineData(5, 6)]
    public void ACaseOutOfTheBag_IsNotFound(int from, int to)
    {
        CharacterInventory inventory = TestItems.Inventory(TestItems.InBag(TestItems.Sword, 0));

        inventory.Move(from, to).Should().Be(ItemOutcome.NotFound);
        inventory.Split(from, 1, to).Should().Be(ItemOutcome.NotFound);

        inventory.Dirty.Should().BeEmpty();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(30)]
    [InlineData(int.MaxValue)]
    [InlineData(5)]
    public void ADestroyOutOfTheBagOrOfAnEmptyCase_IsNotFound(int slot)
    {
        CharacterInventory inventory = TestItems.Inventory(TestItems.InBag(TestItems.Sword, 0));

        inventory.Destroy(slot, 1).Should().Be(ItemOutcome.NotFound);
        inventory.Dirty.Should().BeEmpty();
    }

    [Fact]
    public void ASplit_MakesANewStackWithoutAnId()
    {
        CharacterInventory inventory = TestItems.Inventory(TestItems.InBag(TestItems.Shard, 0, 20));

        inventory.Split(0, 5, 3).Should().Be(ItemOutcome.Ok);

        inventory.At(ItemContainer.Bag, 0)!.Quantity.Should().Be(15);
        ItemInstance part = inventory.At(ItemContainer.Bag, 3)!;
        (part.Id, part.Quantity, part.DefinitionId).Should().Be((0, 5, TestItems.Shard.Id));
        inventory.Dirty.Should().HaveCount(2);
    }

    [Fact]
    public void ASplit_OntoAnOccupiedCase_IsSlotOccupied()
    {
        CharacterInventory inventory = TestItems.Inventory(
            TestItems.InBag(TestItems.Shard, 0, 20),
            TestItems.InBag(TestItems.Sword, 1)
        );

        inventory.Split(0, 5, 1).Should().Be(ItemOutcome.SlotOccupied);
        inventory.Dirty.Should().BeEmpty();
    }

    [Fact]
    public void AQuantityOutOfBounds_IsRefused()
    {
        CharacterInventory inventory = TestItems.Inventory(TestItems.InBag(TestItems.Shard, 0, 20));

        inventory.Split(0, 0, 1).Should().Be(ItemOutcome.InvalidQuantity);
        inventory.Split(0, 20, 1).Should().Be(ItemOutcome.InvalidQuantity);
        inventory.Split(0, -1, 1).Should().Be(ItemOutcome.InvalidQuantity);
        inventory.Destroy(0, 0).Should().Be(ItemOutcome.InvalidQuantity);
        inventory.Destroy(0, 21).Should().Be(ItemOutcome.InvalidQuantity);
        inventory.Destroy(0, int.MaxValue).Should().Be(ItemOutcome.InvalidQuantity);
        inventory.Remove(TestItems.Shard.Id, 0).Outcome.Should().Be(ItemOutcome.InvalidQuantity);
        inventory.Remove(TestItems.Shard.Id, -5).Outcome.Should().Be(ItemOutcome.InvalidQuantity);

        inventory.Dirty.Should().BeEmpty();
        inventory.At(ItemContainer.Bag, 0)!.Quantity.Should().Be(20);
    }

    [Fact]
    public void ADestroy_OfPartOfAStack_KeepsTheRest()
    {
        CharacterInventory inventory = TestItems.Inventory(TestItems.InBag(TestItems.Shard, 0, 20));

        inventory.Destroy(0, 8).Should().Be(ItemOutcome.Ok);

        inventory.At(ItemContainer.Bag, 0)!.Quantity.Should().Be(12);
    }

    [Fact]
    public void ADestroy_OfAWholeStack_DeletesIt()
    {
        CharacterInventory inventory = TestItems.Inventory(TestItems.InBag(TestItems.Sword, 0));

        inventory.Destroy(0, 1).Should().Be(ItemOutcome.Ok);

        inventory.Live.Should().BeEmpty();
        inventory.Dirty.Should().ContainSingle(i => i.Deleted);
    }

    [Fact]
    public void ARemove_TakesFromStacksInOrder_AllOrNothing()
    {
        CharacterInventory inventory = TestItems.Inventory(
            TestItems.InBag(TestItems.Shard, 0, 10),
            TestItems.InBag(TestItems.Shard, 1, 10)
        );

        inventory.Remove(TestItems.Shard.Id, 21).Outcome.Should().Be(ItemOutcome.NotFound);
        inventory.Dirty.Should().BeEmpty();

        (ItemOutcome outcome, var taken) = inventory.Remove(TestItems.Shard.Id, 15);

        outcome.Should().Be(ItemOutcome.Ok);
        taken.Select(t => (t.Item.Slot, t.Quantity)).Should().Equal((0, 10), (1, 5));
        inventory.At(ItemContainer.Bag, 0).Should().BeNull();
        inventory.At(ItemContainer.Bag, 1)!.Quantity.Should().Be(5);
    }

    [Fact]
    public void MarkWritten_DropsDeletedCopiesAndClearsDirty()
    {
        CharacterInventory inventory = TestItems.Inventory(
            TestItems.InBag(TestItems.Sword, 0),
            TestItems.InBag(TestItems.Ring, 1)
        );
        inventory.Destroy(0, 1);
        inventory.Move(1, 2);

        inventory.MarkWritten();

        inventory.Dirty.Should().BeEmpty();
        inventory.Live.Should().ContainSingle().Which.Slot.Should().Be(2);
    }

    [Fact]
    public void AClone_IsIndependent()
    {
        CharacterInventory inventory = TestItems.Inventory(TestItems.InBag(TestItems.Sword, 0));

        CharacterInventory clone = inventory.Clone();
        clone.Move(0, 4);

        inventory.At(ItemContainer.Bag, 0).Should().NotBeNull();
        inventory.Dirty.Should().BeEmpty();
        clone.At(ItemContainer.Bag, 4).Should().NotBeNull();
    }

    [Fact]
    public void TheSnapshot_ListsTheLiveCopiesInOrder()
    {
        CharacterInventory inventory = TestItems.Inventory(
            TestItems.InBag(TestItems.Ring, 3),
            TestItems.InBag(TestItems.Sword, 1)
        );

        inventory
            .Snapshot()
            .Select(s => (s.DefinitionId, s.Slot))
            .Should()
            .Equal((TestItems.Sword.Id, 1), (TestItems.Ring.Id, 3));
    }
}
