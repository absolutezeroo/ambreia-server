using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Vortex.Primitives.Rpg.Content;
using Vortex.Rpg.Content.Kinds;
using Vortex.Rpg.Content.Validation;
using Xunit;

namespace Vortex.Rpg.Tests.Content;

public sealed class ContentPackValidatorTests
{
    private const string Warrior = """
        { "id": "class.warrior", "kind": "class", "role": "tank", "range": 1, "primaryStat": "strength",
          "base": { "strength": 1, "intelligence": 1, "agility": 1, "health": 1, "mana": 1 }, "perLevel": {},
          "specializations": ["spec.warrior.guardian"] }
        """;

    private const string Guardian = """
        { "id": "spec.warrior.guardian", "kind": "specialization", "class": "class.warrior" }
        """;

    private static readonly Dictionary<string, string> Fr = new()
    {
        ["fr"] =
            "class.warrior.name=Guerrier\nclass.warrior.description=x\nspec.warrior.guardian.name=Gardien\nspec.warrior.guardian.description=x",
    };

    private static ContentPackValidator Validator() =>
        new([new ClassKind(), new SpecializationKind(), new ProgressionKind(), new ItemKind()]);

    private static ContentFile F(string path, string json) => new(path, json);

    private static ValidatedPack Run(params ContentFile[] files) =>
        Validator().Validate(new ContentPack(files, null, Fr), _ => true);

    private static ValidatedPack RunWarrior(
        IReadOnlyDictionary<string, string> texts,
        string? migrations = null
    ) =>
        Validator()
            .Validate(
                new ContentPack(
                    [F("class/warrior.json", Warrior), F("specialization/guardian.json", Guardian)],
                    migrations,
                    texts
                ),
                _ => true
            );

    [Fact]
    public void AConsistentPack_HasNoError() =>
        Run(F("class/warrior.json", Warrior), F("specialization/guardian.json", Guardian))
            .HasErrors.Should()
            .BeFalse();

    [Fact]
    public void ABomAndCrlf_AreRead() =>
        Run(
            F("class/warrior.json", "﻿" + Warrior.Replace(",", ",\r\n")),
            F("specialization/guardian.json", Guardian)
        )
            .HasErrors.Should()
            .BeFalse();

    [Fact]
    public void BadJson_NamesTheFile() =>
        Run(F("class/warrior.json", "{ nope"))
            .Findings.Should()
            .ContainSingle(f => f.Code == "Json" && f.File == "class/warrior.json");

    [Fact]
    public void AnUnknownKind_IsAnError() =>
        Run(F("x.json", """{ "id": "spell.fire", "kind": "spell" }"""))
            .Findings.Select(f => f.Code)
            .Should()
            .Contain("UnknownKind");

    [Fact]
    public void AnIdWhosePrefixIsNotItsKind_IsAnError() =>
        Run(
            F("class/w.json", Warrior.Replace("\"class.warrior\"", "\"item.warrior\"")),
            F("specialization/guardian.json", Guardian)
        )
            .Findings.Select(f => f.Code)
            .Should()
            .Contain("IdKindMismatch");

    [Fact]
    public void TheSameIdTwice_NamesBothFiles()
    {
        ValidatedPack pack = Run(
            F("class/a.json", Warrior),
            F("class/b.json", Warrior),
            F("specialization/guardian.json", Guardian)
        );

        pack.Findings.Should()
            .ContainSingle(f => f.Code == "DuplicateId")
            .Which.Message.Should()
            .Contain("class/a.json")
            .And.Contain("class/b.json");
    }

    [Fact]
    public void AMissingTranslation_IsAWarning() =>
        RunWarrior(new Dictionary<string, string> { ["fr"] = "" })
            .Findings.Should()
            .Contain(f => f.Code == "MissingText" && f.Severity == ContentSeverity.Warning);

    [Fact]
    public void NoFrenchTexts_IsAnError() =>
        RunWarrior(new Dictionary<string, string>())
            .Findings.Select(f => f.Code)
            .Should()
            .Contain("NoFrenchTexts");

    [Fact]
    public void AnItemOnAnUnknownFurniture_IsAnError() =>
        Validator()
            .Validate(
                new ContentPack(
                    [
                        F(
                            "item/chair.json",
                            """{ "id": "item.furniture.chair", "kind": "item", "category": "furniture", "rarity": "common", "furnitureDefinition": 999 }"""
                        ),
                    ],
                    null,
                    Fr
                ),
                id => id != 999
            )
            .Findings.Select(f => f.Code)
            .Should()
            .Contain("UnknownFurniture");

    [Fact]
    public void AMigrationToAnotherKind_IsAnError() =>
        RunWarrior(Fr, """[{ "from": "item.old", "action": "replace", "to": "class.warrior" }]""")
            .Findings.Select(f => f.Code)
            .Should()
            .Contain("MigrationKindMismatch");

    [Fact]
    public void ASchemaLine_IsIgnored() =>
        Run(
            F(
                "class/warrior.json",
                Warrior.Replace("{ \"id\"", "{ \"$schema\": \"../schemas/class.json\", \"id\"")
            ),
            F("specialization/guardian.json", Guardian)
        )
            .HasErrors.Should()
            .BeFalse();

    [Fact]
    public void AFindingOfAKind_NamesItsFile() =>
        Run(F("class/warrior.json", Warrior))
            .Findings.Should()
            .Contain(f => f.Code == "BrokenReference" && f.File == "class/warrior.json");
}
