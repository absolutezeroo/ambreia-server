using System.Linq;
using FluentAssertions;
using Vortex.Primitives.Rpg.Items;
using Vortex.Primitives.Rpg.Items.Enums;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Items;
using Xunit;

namespace Vortex.Rpg.Tests.Items;

/// <summary>Spec Objets §7, §8: equipping by slot, class and level, the two rings, binding on
/// equip, and what the worn items add.</summary>
public sealed class CharacterInventoryEquipmentTests
{
    private const string Warrior = "class.warrior";
    private const int WeaponSlot = (int)EquipmentSlot.Weapon;

    [Fact]
    public void AWeapon_GoesToTheWeaponSlot_AndTheWornOneComesBackToTheCase()
    {
        CharacterInventory inventory = TestItems.Inventory(
            TestItems.InBag(TestItems.Sword, 4),
            TestItems.At(TestItems.Blade, ItemContainer.Equipment, WeaponSlot)
        );

        inventory.Equip(4, Warrior, 1).Should().Be(ItemOutcome.Ok);

        inventory
            .At(ItemContainer.Equipment, WeaponSlot)!
            .DefinitionId.Should()
            .Be(TestItems.Sword.Id);
        inventory.At(ItemContainer.Bag, 4)!.DefinitionId.Should().Be(TestItems.Blade.Id);
    }

    [Fact]
    public void Rings_FillRingOneThenRingTwo_ThenSwapRingOne()
    {
        CharacterInventory inventory = TestItems.Inventory(
            TestItems.InBag(TestItems.Ring, 0),
            TestItems.InBag(TestItems.Ring, 1),
            TestItems.InBag(TestItems.Ring, 2)
        );
        int first = inventory.At(ItemContainer.Bag, 0)!.Id;
        int third = inventory.At(ItemContainer.Bag, 2)!.Id;

        inventory.Equip(0, Warrior, 1).Should().Be(ItemOutcome.Ok);
        inventory.Equip(1, Warrior, 1).Should().Be(ItemOutcome.Ok);
        inventory.Equip(2, Warrior, 1).Should().Be(ItemOutcome.Ok);

        inventory.At(ItemContainer.Equipment, EquipmentSlots.Ring1)!.Id.Should().Be(third);
        inventory.At(ItemContainer.Equipment, EquipmentSlots.Ring2).Should().NotBeNull();
        inventory.At(ItemContainer.Bag, 2)!.Id.Should().Be(first);
    }

    [Fact]
    public void AnotherClassesItem_IsWrongClass()
    {
        CharacterInventory inventory = TestItems.Inventory(TestItems.InBag(TestItems.Sword, 0));

        inventory.Equip(0, "class.mage", 1).Should().Be(ItemOutcome.WrongClass);
        inventory.Dirty.Should().BeEmpty();
    }

    [Fact]
    public void AnItemAboveTheLevel_IsLevelTooLow()
    {
        CharacterInventory inventory = TestItems.Inventory(TestItems.InBag(TestItems.Blade, 0));

        inventory.Equip(0, Warrior, 4).Should().Be(ItemOutcome.LevelTooLow);
        inventory.Dirty.Should().BeEmpty();
    }

    [Fact]
    public void AMaterial_IsNotEquipment()
    {
        CharacterInventory inventory = TestItems.Inventory(TestItems.InBag(TestItems.Shard, 0, 3));

        inventory.Equip(0, Warrior, 1).Should().Be(ItemOutcome.NotEquipment);
    }

    [Fact]
    public void AnOnEquipBindingItem_IsBoundWhenEquipped()
    {
        CharacterInventory inventory = TestItems.Inventory(TestItems.InBag(TestItems.Blade, 0));

        inventory.Equip(0, Warrior, 5).Should().Be(ItemOutcome.Ok);

        inventory.At(ItemContainer.Equipment, WeaponSlot)!.Bound.Should().BeTrue();
    }

    [Fact]
    public void AFreeItem_StaysFreeWhenEquipped()
    {
        CharacterInventory inventory = TestItems.Inventory(TestItems.InBag(TestItems.Sword, 0));

        inventory.Equip(0, Warrior, 1);

        inventory.At(ItemContainer.Equipment, WeaponSlot)!.Bound.Should().BeFalse();
    }

    [Fact]
    public void AnUnequip_GoesToTheFirstEmptyCase()
    {
        CharacterInventory inventory = TestItems.Inventory(
            TestItems.InBag(TestItems.Ring, 0),
            TestItems.At(TestItems.Sword, ItemContainer.Equipment, WeaponSlot)
        );

        inventory.Unequip(WeaponSlot).Should().Be(ItemOutcome.Ok);

        inventory.At(ItemContainer.Bag, 1)!.DefinitionId.Should().Be(TestItems.Sword.Id);
        inventory.At(ItemContainer.Equipment, WeaponSlot).Should().BeNull();
    }

    [Fact]
    public void AnUnequip_WithAFullBag_IsBagFullAndChangesNothing()
    {
        CharacterInventory inventory = TestItems.Inventory(
            1,
            50,
            TestItems.InBag(TestItems.Ring, 0),
            TestItems.At(TestItems.Sword, ItemContainer.Equipment, WeaponSlot)
        );

        inventory.Unequip(WeaponSlot).Should().Be(ItemOutcome.BagFull);

        inventory.At(ItemContainer.Equipment, WeaponSlot).Should().NotBeNull();
        inventory.Dirty.Should().BeEmpty();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10)]
    [InlineData(int.MaxValue)]
    [InlineData(0)]
    public void AnEquipmentSlotOutOfRange_IsNotFound(int slot)
    {
        CharacterInventory inventory = TestItems.Inventory(
            TestItems.At(TestItems.Sword, ItemContainer.Equipment, WeaponSlot)
        );

        inventory.Unequip(slot).Should().Be(ItemOutcome.NotFound);
        inventory.Equip(30, Warrior, 1).Should().Be(ItemOutcome.NotFound);
        inventory.Dirty.Should().BeEmpty();
    }

    [Fact]
    public void EquippedStats_LeaveOutBrokenAndUnknownItems()
    {
        ItemInstance broken = TestItems.At(TestItems.Sword, ItemContainer.Equipment, WeaponSlot);
        broken.Durability = 0;
        ItemInstance unknown = new()
        {
            Id = 900,
            DefinitionId = "item.head.gone",
            Quantity = 1,
            Container = ItemContainer.Equipment,
            Slot = (int)EquipmentSlot.Head,
        };
        CharacterInventory inventory = TestItems.Inventory(
            broken,
            unknown,
            TestItems.At(TestItems.Ring, ItemContainer.Equipment, EquipmentSlots.Ring1),
            TestItems.InBag(TestItems.Blade, 0)
        );

        inventory.EquippedStats().Should().Equal(new StatBlock { Agility = 1 });
    }

    [Fact]
    public void AnItemThatNeverWearsOut_GivesItsStats()
    {
        CharacterInventory inventory = TestItems.Inventory(
            TestItems.At(TestItems.Blade, ItemContainer.Equipment, WeaponSlot)
        );

        inventory.EquippedStats().Single().Strength.Should().Be(9);
    }
}
