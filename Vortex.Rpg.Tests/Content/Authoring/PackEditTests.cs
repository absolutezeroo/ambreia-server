using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using FluentAssertions;
using Vortex.Primitives.Rpg.Content;
using Vortex.Rpg.Content.Authoring;
using Vortex.Rpg.Content.Kinds;
using Xunit;

namespace Vortex.Rpg.Tests.Content.Authoring;

/// <summary>Content authoring §4, §5: one document changed in the stored pack, nothing else.</summary>
public sealed class PackEditTests
{
    private const string Sword = """
        {
          "$schema": "../../schemas/content/item.schema.json",
          "id": "item.weapon.rusted_sword",
          "kind": "item",
          "category": "equipment",
          "allowedClasses": ["class.warrior"]
        }

        """;

    private const string Mage = """
        { "id": "class.mage", "kind": "class" }

        """;

    private const string Fr = "# c\nitem.weapon.rusted_sword.name=Épée\nclass.mage.name=Mage\n";

    private static readonly ItemKind Items = new();

    private static readonly ContentExport Stored = new(
        3,
        [
            new ContentFile("item/rusted_sword.json", Sword),
            new ContentFile("class/mage.json", Mage),
        ],
        "[]",
        new Dictionary<string, string> { ["fr"] = Fr }
    );

    private static PackEditResult Put(
        string id,
        string source,
        string? path = null,
        Dictionary<string, IReadOnlyDictionary<string, string>>? texts = null
    ) => PackEdit.Put(Stored, Items, id, path, source, texts);

    [Fact]
    public void ANewDocument_IsAddedAtItsDerivedPath_IndentedWithItsSchemaFirst()
    {
        PackEditResult edit = Put(
            "item.tool.pickaxe",
            """{"id":"item.tool.pickaxe","kind":"item","category":"tool","name":"Pioche à l'œil"}"""
        );

        edit.Error.Should().BeNull();
        edit.Pack!.Files.Should().HaveCount(3);
        edit.Pack.Files.Single(f => f.Path == "item/pickaxe.json")
            .Text.Should()
            .Be(
                "{\n  \"$schema\": \"../../schemas/content/item.schema.json\",\n  \"id\": \"item.tool.pickaxe\",\n  \"kind\": \"item\",\n  \"category\": \"tool\",\n  \"name\": \"Pioche à l'œil\"\n}\n"
            );
        edit.Pack.Migrations.Should().Be("[]");
        edit.Pack.Texts.Should().BeEquivalentTo(Stored.Texts);
    }

    [Fact]
    public void AnUnchangedDocument_GivesThePackBack()
    {
        PackEditResult edit = Put(
            "item.weapon.rusted_sword",
            """{"kind":"item","id":"item.weapon.rusted_sword","category":"equipment","allowedClasses":["class.warrior"]}"""
        );

        edit.Pack!.Files.Should().Equal(Stored.Files);
    }

    [Fact]
    public void AChangedDocument_KeepsItsPath()
    {
        PackEditResult edit = Put(
            "item.weapon.rusted_sword",
            """{"id":"item.weapon.rusted_sword","kind":"item","category":"material"}"""
        );

        ContentFile sword = edit.Pack!.Files.Single(f => f.Text.Contains("rusted_sword"));
        sword.Path.Should().Be("item/rusted_sword.json");
        JsonNode.Parse(sword.Text)!["category"]!.GetValue<string>().Should().Be("material");
        edit.Pack.Files.Should().Contain(new ContentFile("class/mage.json", Mage));
    }

    [Fact]
    public void ASafePathGiven_IsWhereTheDocumentGoes() =>
        Put(
            "item.tool.pickaxe",
            """{"id":"item.tool.pickaxe","kind":"item"}""",
            "item/tools/pickaxe.json"
        )
            .Pack!.Files.Should()
            .Contain(f => f.Path == "item/tools/pickaxe.json");

    [Theory]
    [InlineData("item/../../x.json")]
    [InlineData("monster/pickaxe.json")]
    public void AnUnsafePath_IsRefused(string path) =>
        Put("item.tool.pickaxe", """{"id":"item.tool.pickaxe","kind":"item"}""", path)
            .Should()
            .Be(new PackEditResult(null, "content.bad_path"));

    [Theory]
    [InlineData("item.tool.pickaxe", """{"id":"item.tool.axe","kind":"item"}""")]
    [InlineData("Item.Tool", """{"id":"Item.Tool","kind":"item"}""")]
    [InlineData("monster.wolf", """{"id":"monster.wolf","kind":"item"}""")]
    [InlineData("item.tool.pickaxe", """{"id":"item.tool.pickaxe","kind":"monster"}""")]
    [InlineData("item.tool.pickaxe", """{"kind":"item"}""")]
    public void ADocumentThatIsNotTheUrls_IsRefused(string id, string source) =>
        Put(id, source).Should().Be(new PackEditResult(null, "content.id_mismatch"));

    [Theory]
    [InlineData("[1, 2]")]
    [InlineData("not json")]
    public void ASourceThatIsNoObject_IsRefused(string source) =>
        Put("item.tool.pickaxe", source)
            .Should()
            .Be(new PackEditResult(null, "content.bad_source"));

    [Fact]
    public void TheDocumentsTexts_AreReplacedInPlace_TheOthersKept()
    {
        PackEditResult edit = Put(
            "item.weapon.rusted_sword",
            Sword,
            texts: new()
            {
                ["fr"] = new Dictionary<string, string>
                {
                    ["item.weapon.rusted_sword.name"] = "Épée rouillée",
                    ["item.weapon.rusted_sword.description"] = "Vieille",
                },
            }
        );

        edit.Pack!.Texts["fr"]
            .Should()
            .Be(
                "# c\nitem.weapon.rusted_sword.name=Épée rouillée\nitem.weapon.rusted_sword.description=Vieille\nclass.mage.name=Mage\n"
            );
    }

    [Theory]
    [InlineData("fr", "class.mage.name", "Mage")]
    [InlineData("fr", "item.weapon.rusted_sword.name", "Deux\nlignes")]
    [InlineData("fr", "item.weapon.rusted_sword.name", "retour\r")]
    [InlineData("FR", "item.weapon.rusted_sword.name", "Épée")]
    [InlineData("fr", "item.weapon.rusted_sword.na=me", "Épée")]
    public void ATextOutsideTheDocument_OrOnTwoLines_IsRefused(
        string language,
        string key,
        string value
    ) =>
        Put(
                "item.weapon.rusted_sword",
                Sword,
                texts: new() { [language] = new Dictionary<string, string> { [key] = value } }
            )
            .Should()
            .Be(new PackEditResult(null, "content.bad_text"));

    [Fact]
    public void ANewLanguage_GetsItsOwnTextFile() =>
        Put(
            "item.weapon.rusted_sword",
            Sword,
            texts: new()
            {
                ["en"] = new Dictionary<string, string>
                {
                    ["item.weapon.rusted_sword.name"] = "Rusty sword",
                },
            }
        )
            .Pack!.Texts["en"]
            .Should()
            .Be("item.weapon.rusted_sword.name=Rusty sword\n");

    /// <summary>quest.camp.wolves is under quest.camp: removing quest.camp keeps the wolves' lines.</summary>
    [Fact]
    public void ALongerIdNestedUnderTheDocument_KeepsItsTexts()
    {
        ContentExport stored = new(
            1,
            [
                new ContentFile("quest/camp.json", """{"id":"quest.camp","kind":"quest"}"""),
                new ContentFile(
                    "quest/wolves.json",
                    """{"id":"quest.camp.wolves","kind":"quest"}"""
                ),
            ],
            null,
            new Dictionary<string, string>
            {
                ["fr"] = "quest.camp.name=Camp\nquest.camp.wolves.name=Loups\n",
            }
        );

        PackEdit
            .Remove(stored, "quest.camp", null)
            .Pack!.Texts["fr"]
            .Should()
            .Be("quest.camp.wolves.name=Loups\n");
    }

    [Fact]
    public void ARemovedDocument_LeavesThePack_WithItsTexts()
    {
        PackEditResult edit = PackEdit.Remove(Stored, "item.weapon.rusted_sword", null);

        edit.Pack!.Files.Should().Equal(new ContentFile("class/mage.json", Mage));
        edit.Pack.Texts["fr"].Should().Be("# c\nclass.mage.name=Mage\n");
        edit.Pack.Migrations.Should().Be("[]");
    }

    [Fact]
    public void ARemovalWithAReplacement_AddsTheRule()
    {
        PackEditResult edit = PackEdit.Remove(
            Stored,
            "item.weapon.rusted_sword",
            "item.weapon.axe"
        );

        JsonNode rule = JsonNode.Parse(edit.Pack!.Migrations!)!.AsArray().Single()!;
        (
            rule["from"]!.GetValue<string>(),
            rule["action"]!.GetValue<string>(),
            rule["to"]!.GetValue<string>()
        )
            .Should()
            .Be(("item.weapon.rusted_sword", "replace", "item.weapon.axe"));
    }

    [Fact]
    public void RemovingAnUnknownDocument_IsRefused() =>
        PackEdit
            .Remove(Stored, "item.weapon.ghost", null)
            .Should()
            .Be(new PackEditResult(null, "content.unknown_document"));

    [Fact]
    public void AReplacementThatIsNoId_IsRefused() =>
        PackEdit
            .Remove(Stored, "item.weapon.rusted_sword", "Not An Id")
            .Should()
            .Be(new PackEditResult(null, "content.id_mismatch"));
}
