using FluentAssertions;
using Vortex.Primitives.Rpg.Items.Enums;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Items;
using Xunit;

namespace Vortex.Rpg.Tests.Items;

/// <summary>Spec M1a §8.1: dying wears every equipped item by a share of its max durability.</summary>
public sealed class CharacterInventoryWearTests
{
    private const int WeaponSlot = (int)EquipmentSlot.Weapon;

    [Fact]
    public void Wear_TakesAPercentOfTheMax_DownToZero()
    {
        CharacterInventory inventory = TestItems.Inventory(
            TestItems.At(TestItems.Sword, ItemContainer.Equipment, WeaponSlot)
        );
        ItemInstance sword = inventory.At(ItemContainer.Equipment, WeaponSlot)!;

        inventory.WearEquipped(10);

        sword.Durability.Should().Be(45, "10 % of 50");
        sword.Dirty.Should().BeTrue();

        sword.Durability = 3;
        inventory.WearEquipped(10);

        sword.Durability.Should().Be(0);
    }

    [Fact]
    public void AnItemWithoutDurability_IsNotWorn()
    {
        CharacterInventory inventory = TestItems.Inventory(
            TestItems.At(TestItems.Blade, ItemContainer.Equipment, WeaponSlot)
        );
        ItemInstance blade = inventory.At(ItemContainer.Equipment, WeaponSlot)!;

        inventory.WearEquipped(10).Should().Be((false, false));

        blade.Durability.Should().BeNull();
        blade.Dirty.Should().BeFalse();
    }

    [Fact]
    public void AnItemInTheBag_IsNotWorn()
    {
        CharacterInventory inventory = TestItems.Inventory(TestItems.InBag(TestItems.Sword, 0));

        inventory.WearEquipped(10);

        inventory.At(ItemContainer.Bag, 0)!.Durability.Should().Be(50);
    }

    [Fact]
    public void ReachingZero_IsReported()
    {
        CharacterInventory inventory = TestItems.Inventory(
            TestItems.At(TestItems.Sword, ItemContainer.Equipment, WeaponSlot)
        );

        inventory.WearEquipped(10).Should().Be((true, false));

        inventory.At(ItemContainer.Equipment, WeaponSlot)!.Durability = 5;

        inventory.WearEquipped(10).Should().Be((true, true));
        inventory.WearEquipped(10).Should().Be((false, false), "already at 0: nothing more goes");
    }
}
