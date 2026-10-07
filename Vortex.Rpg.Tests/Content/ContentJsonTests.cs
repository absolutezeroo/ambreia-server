using System.Text.Json;
using FluentAssertions;
using Vortex.Rpg.Content;
using Vortex.Rpg.Content.Definitions;
using Xunit;

namespace Vortex.Rpg.Tests.Content;

/// <summary>
/// A content document is a contract, like a zone (spec Contenu §4.2): an unknown field, a null or a
/// number in quotes is refused rather than guessed.
/// </summary>
public sealed class ContentJsonTests
{
    private const string Sword = """
        { "id": "item.weapon.rusted_sword", "kind": "item", "category": "equipment",
          "rarity": "common", "slot": "weapon", "allowedClasses": ["class.warrior"],
          "stats": { "strength": 3 }, "maxDurability": 50, "sellValue": 4, "binding": "none" }
        """;

    [Fact]
    public void AnItem_Reads()
    {
        ItemDefinition sword = ContentJson.Deserialize<ItemDefinition>(Sword);

        (sword.Slot, sword.Stats!.Strength, sword.MaxStack, sword.Binding)
            .Should()
            .Be((EquipmentSlot.Weapon, 3, 1, ItemBinding.None));
    }

    [Fact]
    public void AnUnknownField_IsRefused() =>
        FluentActions
            .Invoking(() =>
                ContentJson.Deserialize<ItemDefinition>(Sword.Replace("\"sellValue\"", "\"price\""))
            )
            .Should()
            .Throw<JsonException>();

    [Fact]
    public void ANull_IsRefused() =>
        FluentActions
            .Invoking(() =>
                ContentJson.Deserialize<ItemDefinition>(
                    Sword.Replace("[\"class.warrior\"]", "null")
                )
            )
            .Should()
            .Throw<JsonException>();

    [Fact]
    public void ANumberInQuotes_IsRefused() =>
        FluentActions
            .Invoking(() => ContentJson.Deserialize<ItemDefinition>(Sword.Replace("4,", "\"4\",")))
            .Should()
            .Throw<JsonException>();

    [Fact]
    public void EnumsAreWrittenInSnakeCase() =>
        JsonSerializer
            .Serialize(
                ContentJson.Deserialize<ItemDefinition>(
                    Sword.Replace("\"binding\": \"none\"", "\"binding\": \"on_equip\"")
                ),
                ContentJson.Options
            )
            .Should()
            .Contain("\"binding\":\"on_equip\"")
            .And.Contain("\"slot\":\"weapon\"");

    [Fact]
    public void AClass_Reads()
    {
        ClassDefinition warrior = ContentJson.Deserialize<ClassDefinition>(
            """
            { "id": "class.warrior", "kind": "class", "role": "tank", "range": 1,
              "threatMultiplier": 3, "primaryStat": "strength",
              "base": { "strength": 12, "intelligence": 4, "agility": 6, "health": 120, "mana": 40 },
              "perLevel": { "strength": 3, "health": 18 },
              "specializations": ["spec.warrior.guardian"] }
            """
        );

        (warrior.Role, warrior.PrimaryStat, warrior.Base.Health, warrior.PerLevel.Mana)
            .Should()
            .Be((ClassRole.Tank, PrimaryStat.Strength, 120, 0));
    }
}
