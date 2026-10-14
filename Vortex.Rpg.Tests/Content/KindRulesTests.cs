using System.Linq;
using FluentAssertions;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Content.Kinds;
using Vortex.Rpg.Content.Validation;
using Xunit;

namespace Vortex.Rpg.Tests.Content;

/// <summary>
/// The rules of each kind (spec Personnage §4–5, spec Objets §4): one test per rule, each on a
/// valid definition with one field broken.
/// </summary>
public sealed class KindRulesTests
{
    private static readonly ClassDefinition Warrior = new()
    {
        Id = "class.warrior",
        Kind = "class",
        Role = ClassRole.Tank,
        Range = 1,
        ThreatMultiplier = 3,
        PrimaryStat = PrimaryStat.Strength,
        Base = new StatBlock
        {
            Strength = 12,
            Intelligence = 4,
            Agility = 6,
            Health = 120,
            Mana = 40,
        },
        PerLevel = new StatBlock
        {
            Strength = 3,
            Intelligence = 1,
            Agility = 1,
            Health = 18,
            Mana = 4,
        },
        Specializations = ["spec.warrior.guardian"],
    };

    private static readonly SpecializationDefinition Guardian = new()
    {
        Id = "spec.warrior.guardian",
        Kind = "specialization",
        Class = "class.warrior",
    };

    private static readonly ItemDefinition Sword = new()
    {
        Id = "item.weapon.rusted_sword",
        Kind = "item",
        Category = ItemCategory.Equipment,
        Rarity = ItemRarity.Common,
        Slot = EquipmentSlot.Weapon,
        AllowedClasses = ["class.warrior"],
        Stats = new StatBlock { Strength = 3 },
        MaxDurability = 50,
    };

    private static readonly ProgressionDefinition Curve = new()
    {
        Id = "progression.default",
        Kind = "progression",
        MaxLevel = 3,
        XpToNext = [100, 150],
        HealthPerStrength = 5,
        ManaPerIntelligence = 5,
        CritBasePercent = 5,
        CritPerAgilityPercent = 0.2,
        CritCapPercent = 50,
        DodgePerAgilityPercent = 0.1,
        DodgeCapPercent = 30,
    };

    /// <summary>The finding codes of one definition, among the others; furniture 230 exists.</summary>
    private static string[] Codes(
        IContentKind kind,
        ContentDefinition definition,
        params ContentDefinition[] others
    ) =>
        [
            .. kind.Check(
                    definition,
                    new ContentCheck(
                        others.Append(definition).ToDictionary(d => d.Id),
                        id => id == 230
                    )
                )
                .Select(f => f.Code),
        ];

    [Fact]
    public void AValidClass_HasNoFinding() =>
        Codes(new ClassKind(), Warrior, Guardian).Should().BeEmpty();

    /// <summary>Every new avatar starts in THE starter class: two would make the start a guess.</summary>
    [Fact]
    public void TwoStarterClasses_IsAnError() =>
        Codes(
                new ClassKind(),
                Warrior with
                {
                    Starter = true,
                },
                Guardian,
                Warrior with
                {
                    Id = "class.novice",
                    Starter = true,
                }
            )
            .Should()
            .Contain("SeveralStarterClasses");

    [Fact]
    public void OneStarterClass_IsFine() =>
        Codes(new ClassKind(), Warrior with { Starter = true }, Guardian).Should().BeEmpty();

    [Fact]
    public void AClassWithoutSpecialization_IsAnError() =>
        Codes(new ClassKind(), Warrior with { Specializations = [] })
            .Should()
            .Equal("NoSpecialization");

    [Fact]
    public void AClassWithANegativeGrowth_IsAnError() =>
        Codes(
                new ClassKind(),
                Warrior with
                {
                    PerLevel = Warrior.PerLevel with { Mana = -1 },
                },
                Guardian
            )
            .Should()
            .Equal("NegativeStat");

    [Fact]
    public void AClassWithAZeroBase_IsAnError() =>
        Codes(new ClassKind(), Warrior with { Base = Warrior.Base with { Health = 0 } }, Guardian)
            .Should()
            .Equal("NonPositiveBase");

    [Fact]
    public void AClassWithNoReach_IsAnError() =>
        Codes(new ClassKind(), Warrior with { Range = 0 }, Guardian).Should().Equal("BadRange");

    [Fact]
    public void ASpecializationOfAnotherClass_IsAnError() =>
        Codes(new ClassKind(), Warrior, Guardian with { Class = "class.mage" })
            .Should()
            .Equal("SpecializationOfAnotherClass");

    [Fact]
    public void ASpecializationOfAnUnknownClass_IsABrokenReference() =>
        Codes(new SpecializationKind(), Guardian).Should().Equal("BrokenReference");

    [Fact]
    public void AValidItem_HasNoFinding() =>
        Codes(new ItemKind(), Sword, Warrior).Should().BeEmpty();

    private static readonly ItemDefinition Pickaxe = Sword with
    {
        Id = "item.tool.pickaxe",
        Category = ItemCategory.Tool,
        Slot = null,
        AllowedClasses = [],
        Stats = null,
        MaxDurability = 0,
    };

    /// <summary>Content authoring §7: a tool is an item that is not worn.</summary>
    [Fact]
    public void ATool_IsAnItemWithoutSlot() => Codes(new ItemKind(), Pickaxe).Should().BeEmpty();

    [Fact]
    public void ASlotOnATool_IsAnError() =>
        Codes(new ItemKind(), Pickaxe with { Slot = EquipmentSlot.Weapon })
            .Should()
            .Equal("SlotForbidden");

    [Fact]
    public void AnEquipmentWithoutSlot_IsAnError() =>
        Codes(new ItemKind(), Sword with { Slot = null }, Warrior).Should().Equal("SlotRequired");

    [Fact]
    public void ASlotOnAMaterial_IsAnError() =>
        Codes(
                new ItemKind(),
                Sword with
                {
                    Category = ItemCategory.Material,
                    Stats = null,
                    MaxDurability = 0,
                },
                Warrior
            )
            .Should()
            .Equal("SlotForbidden");

    [Fact]
    public void AStackOfEquipment_IsAnError() =>
        Codes(new ItemKind(), Sword with { MaxStack = 5 }, Warrior)
            .Should()
            .Equal("EquipmentStacks");

    [Fact]
    public void AStackBeyond999_IsAnError() =>
        Codes(
                new ItemKind(),
                Sword with
                {
                    Category = ItemCategory.Material,
                    Slot = null,
                    Stats = null,
                    MaxDurability = 0,
                    MaxStack = 1000,
                },
                Warrior
            )
            .Should()
            .Equal("BadStack");

    [Fact]
    public void ARareEquipmentWithoutBinding_IsAnError() =>
        Codes(new ItemKind(), Sword with { Rarity = ItemRarity.Rare }, Warrior)
            .Should()
            .Equal("RareMustBind");

    [Fact]
    public void AQuestItemNotBoundOnPickup_IsAnError() =>
        Codes(
                new ItemKind(),
                Sword with
                {
                    Category = ItemCategory.Quest,
                    Slot = null,
                    Stats = null,
                    MaxDurability = 0,
                },
                Warrior
            )
            .Should()
            .Equal("QuestMustBind");

    [Fact]
    public void AnUnknownClass_IsABrokenReference() =>
        Codes(new ItemKind(), Sword with { AllowedClasses = ["class.paladin"] })
            .Should()
            .Equal("BrokenReference");

    [Fact]
    public void AFurnitureWithoutItsDefinition_IsAnError() =>
        Codes(new ItemKind(), Furniture(null)).Should().Equal("FurnitureRequired");

    [Fact]
    public void AFurnitureNotInTheCatalogue_IsAnError() =>
        Codes(new ItemKind(), Furniture(999)).Should().Equal("UnknownFurniture");

    [Fact]
    public void AFurnitureInTheCatalogue_IsValid() =>
        Codes(new ItemKind(), Furniture(230)).Should().BeEmpty();

    [Fact]
    public void ANegativeValue_IsAnError() =>
        Codes(new ItemKind(), Sword with { SellValue = -1 }, Warrior)
            .Should()
            .Equal("NegativeValue");

    [Fact]
    public void ALevelBelowOne_IsAnError() =>
        Codes(new ItemKind(), Sword with { RequiredLevel = 0 }, Warrior).Should().Equal("BadLevel");

    [Fact]
    public void ALevelAboveTheMaxLevel_IsAnError() =>
        Codes(new ItemKind(), Sword with { RequiredLevel = 4 }, Warrior, Curve)
            .Should()
            .Equal("RequiredLevelAboveMax");

    [Fact]
    public void AValidCurve_HasNoFinding() =>
        Codes(new ProgressionKind(), Curve).Should().BeEmpty();

    [Fact]
    public void AShortXpCurve_IsAnError() =>
        Codes(new ProgressionKind(), Curve with { XpToNext = [100] })
            .Should()
            .Equal("XpCurveLength");

    [Fact]
    public void ADecreasingXpCurve_IsAnError() =>
        Codes(new ProgressionKind(), Curve with { XpToNext = [200, 100] })
            .Should()
            .Equal("XpCurveNotIncreasing");

    [Fact]
    public void AMaxLevelBelowTwo_IsAnError() =>
        Codes(new ProgressionKind(), Curve with { MaxLevel = 1, XpToNext = [] })
            .Should()
            .Equal("BadMaxLevel");

    [Fact]
    public void ACapAbove100_IsAnError() =>
        Codes(new ProgressionKind(), Curve with { CritCapPercent = 120 })
            .Should()
            .Equal("BadPercent");

    private static ItemDefinition Furniture(int? definition) =>
        new()
        {
            Id = "item.furniture.throne",
            Kind = "item",
            Category = ItemCategory.Furniture,
            Rarity = ItemRarity.Common,
            FurnitureDefinition = definition,
        };
}
