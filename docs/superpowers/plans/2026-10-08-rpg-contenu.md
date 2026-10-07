# RPG F2 — Contenu : plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Le contenu du RPG (classes, spécialisations, courbe d'XP, objets) devient des documents JSON à id stable, validés tout ou rien à l'import depuis `content/`, servis par un registre en mémoire rechargé à chaud.

**Architecture:** Nouveau projet `Vortex.Rpg` (couche plateforme) : records des définitions, sortes (`IContentKind`) déclarées par DI, validation pure en classes séparées, registre `IRpgContent` (instantané immuable, `IReferenceDataProvider` d'étape 1), service d'import transactionnel. Trois tables (`rpg_content`, `rpg_content_packs`, `rpg_content_migrations`) et une pour les textes (`rpg_content_texts`) dans l'`InitialCreate` régénérée. Deux routes du web API, un script `scripts/content/push.mjs`, et un paquet de démonstration dans `content/`.

**Tech Stack:** C# / .NET 10, Orleans (silo unique), EF Core 9 + Pomelo MySQL (SQLite en test), System.Text.Json + `JsonSchemaExporter`, xUnit + FluentAssertions, Node 24 pour le script.

**Spec:** `docs/superpowers/specs/2026-10-08-rpg-content-design.md` (et, pour les champs des sortes, `2026-10-08-rpg-character-design.md` §4–§5 et `2026-10-08-rpg-items-design.md` §4).

## Global Constraints

- Id : `<sorte>.<segment>[.<segment>…]`, segments `[a-z0-9_]+`, séparés par `.`, 64 caractères au plus ; premier segment = sorte.
- JSON strict comme `ZoneJson` : nombres stricts, champ inconnu refusé, `null` refusé, énumérations en texte snake_case (`on_pickup`), jamais en nombre.
- Un type par fichier (C# et TS), sans exception.
- Import **tout ou rien** : une erreur → rien écrit, registre intact.
- Le serveur n'envoie que des clés : `<id>.name`, `<id>.description` ; traductions dans `content/texts/<langue>.txt` (`fr` obligatoire), clé manquante = avertissement.
- Une seule migration `InitialCreate`, régénérée par `node .claude/skills/changing-the-schema/scripts/regenerate-initialcreate.mjs`.
- Ne jamais afficher le mot de passe de la base ; `MYSQL_PWD`.
- Fichiers modifiés avec les outils Edit/Write (pas de script qui réécrit du code).
- Commits sur `main`, chemins explicites (`git commit -o …`), messages en français + lignes Co-Authored-By / Claude-Session.
- Capability `content.publish` ; rôle `owner` (joker `*`) l'a déjà.
- Le registre est en mémoire (amendement §2.1 de la spec) : pas de `RpgContentGrain`.

## Review Focus

1. **Un fichier JSON avec un BOM UTF-8 ou des fins de ligne CRLF** (édité sous Windows) → importé comme les autres, pas « JSON invalide ».
2. **Deux fichiers qui déclarent le même id** (copier-coller) → erreur qui nomme les deux fichiers, rien n'est écrit.
3. **Un objet qui référence un meuble absent du catalogue** → erreur à l'import ; au démarrage, le registre charge après le catalogue (étape 1 > 0) et ne voit pas un faux « meuble inconnu ».
4. **Base vide au premier démarrage** → l'émulateur démarre avec un registre vide, sans erreur.
5. **Import interrompu au milieu de l'écriture** (exception SQL) → la transaction annule tout, le registre garde l'ancien contenu, la version n'avance pas.

Chaque ligne a son test dans la tâche qui possède le code (Tâches 4, 4, 4, 6, 7).

---

## Structure des fichiers

```
Vortex.Primitives/Rpg/Content/            contrats partagés avec le web API
  ContentFile.cs  ContentPack.cs  ContentFinding.cs  ContentSeverity.cs
  ContentImportResult.cs  IRpgContentService.cs
Vortex.Primitives/Events/Rpg/ContentReloadedEvent.cs
Vortex.Primitives/Permissions/Capabilities.cs          (+ Content.Publish)
Vortex.Rpg/                                            nouveau projet (plateforme)
  Vortex.Rpg.csproj  RpgModule.cs  RpgConfig.cs
  Content/ContentIds.cs  Content/ContentJson.cs  Content/IRpgContent.cs
  Content/Definitions/  ContentDefinition.cs ClassDefinition.cs SpecializationDefinition.cs
                        ProgressionDefinition.cs ItemDefinition.cs StatBlock.cs ClassRole.cs
                        PrimaryStat.cs ItemCategory.cs ItemRarity.cs EquipmentSlot.cs ItemBinding.cs
  Content/Kinds/        IContentKind.cs ContentKind.cs ClassKind.cs SpecializationKind.cs
                        ProgressionKind.cs ItemKind.cs
  Content/Validation/   ContentCheck.cs ContentParser.cs ContentPackValidator.cs ContentTexts.cs
  Content/Migrations/   ContentMigrationRule.cs ContentMigrationAction.cs IContentReferences.cs
  Content/Registry/     ContentSet.cs RpgContentRegistry.cs ContentInvalidException.cs
  Content/Import/       RpgContentService.cs
Vortex.Rpg.Tests/                                     nouveau projet de tests (règles pures)
Vortex.Database/Entities/Rpg/  RpgContentEntity.cs RpgContentPackEntity.cs
                               RpgContentMigrationEntity.cs RpgContentTextEntity.cs
Vortex.Database.Tests/Rpg/     RpgContentRegistryTests.cs RpgContentServiceTests.cs
Vortex.Hosting.Tests/Rpg/      ContentSchemaTests.cs ContentPackInRepoTests.cs
Vortex.WebApi/Hosting/WebApiEndpoints.Content.cs
schemas/content/<sorte>.schema.json
content/  class/*.json specialization/*.json progression/default.json item/*.json
          texts/fr.txt migrations.json
scripts/content/push.mjs
scripts/sql/2026-10-08-rpg-content-tables.sql         (la base déjà créée du propriétaire)
```

---

### Task 1: Projet `Vortex.Rpg`, son projet de tests, et les ids de contenu

**Files:**
- Create: `Vortex.Rpg/Vortex.Rpg.csproj`, `Vortex.Rpg/Content/ContentIds.cs`
- Create: `Vortex.Rpg.Tests/Vortex.Rpg.Tests.csproj`, `Vortex.Rpg.Tests/Content/ContentIdsTests.cs`
- Modify: `Vortex.Cloud.sln` (via `dotnet sln add`), `scripts/hooks/check-architecture-walls.mjs:243` (`PLATFORM` gagne `'Rpg'`)

**Interfaces:**
- Produces: `ContentIds.IsValid(string id) : bool`, `ContentIds.KindOf(string id) : string`, `ContentIds.MaxLength = 64`.

- [ ] **Step 1: Créer les projets**

`Vortex.Rpg/Vortex.Rpg.csproj` (copie de `Vortex.World/Vortex.World.csproj`) :

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Hosting" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Vortex.Database\Vortex.Database.csproj" />
    <ProjectReference Include="..\Vortex.Primitives\Vortex.Primitives.csproj" />
  </ItemGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="Vortex.Rpg.Tests" />
    <InternalsVisibleTo Include="Vortex.Database.Tests" />
    <InternalsVisibleTo Include="Vortex.Hosting.Tests" />
  </ItemGroup>
</Project>
```

`Vortex.Rpg.Tests/Vortex.Rpg.Tests.csproj` : copie de `Vortex.Database.Tests.csproj` (mêmes paquets de test), références `Vortex.Rpg` et `Vortex.Tests.Support` seulement.

Run: `dotnet sln Vortex.Cloud.sln add Vortex.Rpg/Vortex.Rpg.csproj Vortex.Rpg.Tests/Vortex.Rpg.Tests.csproj`

Dans `scripts/hooks/check-architecture-walls.mjs`, ligne `const PLATFORM = [...]` : ajouter `'Rpg'`.

- [ ] **Step 2: Écrire les tests**

```csharp
public sealed class ContentIdsTests
{
    [Theory]
    [InlineData("class.warrior")]
    [InlineData("item.weapon.rusted_sword")]
    [InlineData("progression.default")]
    public void AWellFormedId_IsValid(string id) => ContentIds.IsValid(id).Should().BeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("warrior")]                 // no kind segment
    [InlineData("Class.warrior")]           // upper case
    [InlineData("item..sword")]             // empty segment
    [InlineData("item.rusted-sword")]       // dash
    [InlineData("item.sword.")]             // trailing dot
    public void AMalformedId_IsNot(string id) => ContentIds.IsValid(id).Should().BeFalse();

    [Fact]
    public void AnIdLongerThan64_IsNot() =>
        ContentIds.IsValid("item." + new string('a', 60)).Should().BeFalse();

    [Fact]
    public void TheKind_IsTheFirstSegment() =>
        ContentIds.KindOf("item.weapon.rusted_sword").Should().Be("item");
}
```

- [ ] **Step 3: Vérifier l'échec** — Run: `dotnet test Vortex.Rpg.Tests` · Expected: échec de compilation (`ContentIds` n'existe pas).

- [ ] **Step 4: Implémenter**

```csharp
using System.Text.RegularExpressions;

namespace Vortex.Rpg.Content;

/// <summary>Stable content ids (spec Contenu §4.1): kind.segment[.segment…], [a-z0-9_] per segment, 64 max.</summary>
public static partial class ContentIds
{
    public const int MaxLength = 64;

    [GeneratedRegex("^[a-z0-9_]+(\\.[a-z0-9_]+)+$")]
    private static partial Regex Shape();

    public static bool IsValid(string id) => id.Length <= MaxLength && Shape().IsMatch(id);

    /// <summary>The kind an id declares: its first segment.</summary>
    public static string KindOf(string id) => id[..id.IndexOf('.')];
}
```

- [ ] **Step 5: Vérifier le succès** — Run: `dotnet test Vortex.Rpg.Tests` · Expected: PASS (11 tests). Puis `node scripts/hooks/check-architecture-walls.mjs` · Expected: aucun échec.

- [ ] **Step 6: Commit** — `git add Vortex.Rpg Vortex.Rpg.Tests` puis `git commit -o Vortex.Cloud.sln scripts/hooks/check-architecture-walls.mjs Vortex.Rpg Vortex.Rpg.Tests -m "rpg : projet Vortex.Rpg et ids de contenu stables"`

---

### Task 2: Les définitions et leur JSON strict

**Files:**
- Create: `Vortex.Rpg/Content/ContentJson.cs`, `Vortex.Rpg/Content/Definitions/*.cs` (un type par fichier : `ContentDefinition`, `ClassDefinition`, `SpecializationDefinition`, `ProgressionDefinition`, `ItemDefinition`, `StatBlock`, `ClassRole`, `PrimaryStat`, `ItemCategory`, `ItemRarity`, `EquipmentSlot`, `ItemBinding`)
- Test: `Vortex.Rpg.Tests/Content/ContentJsonTests.cs`

**Interfaces:**
- Produces: `ContentJson.Options : JsonSerializerOptions` ; `ContentJson.Deserialize<T>(string json) : T` (lève `JsonException`) ; records de §4 des specs avec ces noms de propriétés (JSON en camelCase) :
  - `ContentDefinition` (abstract) : `required string Id`, `required string Kind`, `bool Retired`.
  - `ClassDefinition` : `ClassRole Role`, `int Range`, `int ThreatMultiplier = 1`, `PrimaryStat PrimaryStat`, `StatBlock Base`, `StatBlock PerLevel`, `List<string> Specializations`.
  - `SpecializationDefinition` : `required string Class`.
  - `ProgressionDefinition` : `int MaxLevel`, `List<int> XpToNext`, `int HealthPerStrength`, `int ManaPerIntelligence`, `double CritBasePercent`, `double CritPerAgilityPercent`, `double CritCapPercent`, `double DodgePerAgilityPercent`, `double DodgeCapPercent`.
  - `ItemDefinition` : `ItemCategory Category`, `ItemRarity Rarity`, `EquipmentSlot? Slot`, `int RequiredLevel = 1`, `List<string> AllowedClasses`, `StatBlock? Stats`, `int MaxDurability`, `int MaxStack = 1`, `int SellValue`, `ItemBinding Binding`, `int? FurnitureDefinition`.
  - `StatBlock` : `int Strength`, `Intelligence`, `Agility`, `Health`, `Mana`, `Armor`, `Speed` (défaut 0).
  - Énumérations : `ClassRole {Tank, Damage, Heal}`, `PrimaryStat {Strength, Intelligence, Agility}`, `ItemCategory {Equipment, Consumable, Material, Furniture, Mount, Cosmetic, Quest}`, `ItemRarity {Common, Uncommon, Rare, Epic, Legendary}`, `EquipmentSlot {Head, Chest, Legs, Feet, Hands, Weapon, Offhand, Neck, Ring}`, `ItemBinding {None, OnPickup, OnEquip}`. Valeurs explicites (`Head = 0` …), jamais renumérotées.

- [ ] **Step 1: Écrire les tests**

```csharp
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
            .Should().Be((EquipmentSlot.Weapon, 3, 1, ItemBinding.None));
    }

    [Fact]
    public void AnUnknownField_IsRefused() =>
        FluentActions.Invoking(() => ContentJson.Deserialize<ItemDefinition>(Sword.Replace("\"sellValue\"", "\"price\"")))
            .Should().Throw<JsonException>();

    [Fact]
    public void ANull_IsRefused() =>
        FluentActions.Invoking(() => ContentJson.Deserialize<ItemDefinition>(Sword.Replace("[\"class.warrior\"]", "null")))
            .Should().Throw<JsonException>();

    [Fact]
    public void ANumberInQuotes_IsRefused() =>
        FluentActions.Invoking(() => ContentJson.Deserialize<ItemDefinition>(Sword.Replace("4,", "\"4\",")))
            .Should().Throw<JsonException>();

    [Fact]
    public void EnumsAreWrittenInCamelCase() =>
        JsonSerializer.Serialize(ContentJson.Deserialize<ItemDefinition>(Sword), ContentJson.Options)
            .Should().Contain("\"binding\":\"none\"").And.Contain("\"slot\":\"weapon\"");
}
```

- [ ] **Step 2: Vérifier l'échec** — `dotnet test Vortex.Rpg.Tests --filter ContentJsonTests` · Expected: échec de compilation.

- [ ] **Step 3: Implémenter** — `ContentJson` copie `Vortex.Primitives/World/ZoneJson.cs` avec `new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)` et :

```csharp
public static T Deserialize<T>(string json) =>
    JsonSerializer.Deserialize<T>(json, Options) ?? throw new JsonException("The document is null.");
```

Les records : un fichier chacun, `sealed record`, propriétés `{ get; init; }`, `List<…>` initialisées à `[]`.

- [ ] **Step 4: Vérifier le succès** — même commande · Expected: PASS (5).
- [ ] **Step 5: Commit** — `rpg : définitions de contenu et JSON strict`.

---

### Task 3: Les sortes et leurs règles

**Files:**
- Create: `Vortex.Primitives/Rpg/Content/ContentFinding.cs`, `ContentSeverity.cs`
- Create: `Vortex.Rpg/Content/Kinds/IContentKind.cs`, `ContentKind.cs`, `ClassKind.cs`, `SpecializationKind.cs`, `ProgressionKind.cs`, `ItemKind.cs`, `Vortex.Rpg/Content/Validation/ContentCheck.cs`
- Test: `Vortex.Rpg.Tests/Content/KindRulesTests.cs`

**Interfaces:**
- Produces:
  - `record ContentFinding(ContentSeverity Severity, string Code, string? File, string? ContentId, string Message)` ; `enum ContentSeverity { Error = 0, Warning = 1 }` (converti en texte).
  - `IContentKind` : `string Kind`, `Type DefinitionType`, `ContentDefinition Parse(string json)`, `IEnumerable<ContentFinding> Check(ContentDefinition definition, ContentCheck check)`.
  - `abstract class ContentKind<T> : IContentKind where T : ContentDefinition` avec `protected abstract IEnumerable<ContentFinding> Check(T definition, ContentCheck check)` et les aides `Error(code, id, message)`, `Warning(...)`.
  - `sealed class ContentCheck(IReadOnlyDictionary<string, ContentDefinition> all, Func<int, bool> furnitureExists)` : `T? Get<T>(string id)`, `bool FurnitureExists(int definition)`.

- [ ] **Step 1: Écrire les tests** — un test par règle des specs, chacun sur une définition valide modifiée d'un champ :

```csharp
public sealed class KindRulesTests
{
    private static readonly ClassDefinition Warrior = new()
    {
        Id = "class.warrior", Kind = "class", Role = ClassRole.Tank, Range = 1, ThreatMultiplier = 3,
        PrimaryStat = PrimaryStat.Strength,
        Base = new StatBlock { Strength = 12, Intelligence = 4, Agility = 6, Health = 120, Mana = 40 },
        PerLevel = new StatBlock { Strength = 3, Intelligence = 1, Agility = 1, Health = 18, Mana = 4 },
        Specializations = ["spec.warrior.guardian"],
    };
    private static readonly SpecializationDefinition Guardian = new() { Id = "spec.warrior.guardian", Kind = "specialization", Class = "class.warrior" };
    private static readonly ItemDefinition Sword = new()
    {
        Id = "item.weapon.rusted_sword", Kind = "item", Category = ItemCategory.Equipment,
        Rarity = ItemRarity.Common, Slot = EquipmentSlot.Weapon, AllowedClasses = ["class.warrior"],
        Stats = new StatBlock { Strength = 3 }, MaxDurability = 50,
    };

    private static string[] Codes(IContentKind kind, ContentDefinition d, params ContentDefinition[] others) =>
        [.. kind.Check(d, new ContentCheck(others.Append(d).ToDictionary(x => x.Id), id => id == 230)).Select(f => f.Code)];

    [Fact] public void AValidClass_HasNoFinding() => Codes(new ClassKind(), Warrior, Guardian).Should().BeEmpty();
    [Fact] public void AClassWithoutSpecialization_IsAnError() => Codes(new ClassKind(), Warrior with { Specializations = [] }).Should().Equal("NoSpecialization");
    [Fact] public void AClassWithANegativeGrowth_IsAnError() => Codes(new ClassKind(), Warrior with { PerLevel = Warrior.PerLevel with { Mana = -1 } }, Guardian).Should().Equal("NegativeStat");
    [Fact] public void ASpecializationOfAnotherClass_IsAnError() => Codes(new ClassKind(), Warrior, Guardian with { Class = "class.mage" }).Should().Equal("SpecializationOfAnotherClass");
    [Fact] public void AnEquipmentWithoutSlot_IsAnError() => Codes(new ItemKind(), Sword with { Slot = null }, Warrior).Should().Equal("SlotRequired");
    [Fact] public void ASlotOnAMaterial_IsAnError() => Codes(new ItemKind(), Sword with { Category = ItemCategory.Material, Stats = null }, Warrior).Should().Equal("SlotForbidden");
    [Fact] public void AStackOfEquipment_IsAnError() => Codes(new ItemKind(), Sword with { MaxStack = 5 }, Warrior).Should().Equal("EquipmentStacks");
    [Fact] public void ARareEquipmentWithoutBinding_IsAnError() => Codes(new ItemKind(), Sword with { Rarity = ItemRarity.Rare }, Warrior).Should().Equal("RareMustBind");
    [Fact] public void AnUnknownClass_IsABrokenReference() => Codes(new ItemKind(), Sword with { AllowedClasses = ["class.paladin"] }).Should().Equal("BrokenReference");
    [Fact] public void AFurnitureNotInTheCatalogue_IsAnError() => Codes(new ItemKind(), Sword with { Category = ItemCategory.Furniture, Slot = null, Stats = null, MaxDurability = 0, FurnitureDefinition = 999 }).Should().Equal("UnknownFurniture");
    [Fact] public void AShortXpCurve_IsAnError() => Codes(new ProgressionKind(), new ProgressionDefinition { Id = "progression.default", Kind = "progression", MaxLevel = 3, XpToNext = [100] }).Should().Equal("XpCurveLength");
    [Fact] public void ADecreasingXpCurve_IsAnError() => Codes(new ProgressionKind(), new ProgressionDefinition { Id = "progression.default", Kind = "progression", MaxLevel = 3, XpToNext = [200, 100] }).Should().Equal("XpCurveNotIncreasing");
}
```

Règles exactes (codes) à implémenter, et seulement celles-ci :
  - `ClassKind` : `Range < 1` → `BadRange` ; une valeur de `Base` ≤ 0 → `NonPositiveBase` ; une valeur de `PerLevel` < 0 → `NegativeStat` ; `Specializations` vide → `NoSpecialization` ; spécialisation inconnue → `BrokenReference` ; spécialisation dont `Class` ≠ cet id → `SpecializationOfAnotherClass`.
  - `SpecializationKind` : `Class` inconnue ou d'une autre sorte → `BrokenReference`.
  - `ProgressionKind` : `MaxLevel` < 2 → `BadMaxLevel` ; `XpToNext.Count != MaxLevel - 1` → `XpCurveLength` ; une valeur ≤ 0 ou non strictement croissante → `XpCurveNotIncreasing` ; un pourcentage négatif ou un plafond > 100 → `BadPercent`.
  - `ItemKind` : `Equipment` sans `Slot` → `SlotRequired` ; autre catégorie avec `Slot` ou `Stats` → `SlotForbidden` ; `Equipment` avec `MaxStack != 1` → `EquipmentStacks` ; `MaxStack` hors 1–999 → `BadStack` ; `RequiredLevel` < 1 → `BadLevel` ; `MaxDurability`, `SellValue` < 0 → `NegativeValue` ; `Equipment` de rareté ≥ `Rare` avec `Binding == None` → `RareMustBind` ; `Quest` sans `OnPickup` → `QuestMustBind` ; `Furniture` sans `FurnitureDefinition` → `FurnitureRequired` ; `FurnitureDefinition` absent du catalogue → `UnknownFurniture` ; classe autorisée inconnue ou d'une autre sorte → `BrokenReference`.

- [ ] **Step 2: Vérifier l'échec** — `dotnet test Vortex.Rpg.Tests --filter KindRulesTests` · Expected: échec de compilation.
- [ ] **Step 3: Implémenter** les sortes. `ContentKind<T>.Parse(json) => ContentJson.Deserialize<T>(json)`.
- [ ] **Step 4: Vérifier le succès** · Expected: PASS (12).
- [ ] **Step 5: Commit** — `rpg : sortes de contenu et leurs règles`.

---

### Task 4: Lecture et validation d'un paquet entier

**Files:**
- Create: `Vortex.Primitives/Rpg/Content/ContentFile.cs`, `ContentPack.cs`
- Create: `Vortex.Rpg/Content/Validation/ContentParser.cs`, `ContentPackValidator.cs`, `ContentTexts.cs`
- Create: `Vortex.Rpg/Content/Migrations/ContentMigrationRule.cs`, `ContentMigrationAction.cs`
- Test: `Vortex.Rpg.Tests/Content/ContentPackValidatorTests.cs`

**Interfaces:**
- Consumes: `IContentKind`, `ContentCheck`, `ContentFinding` (Task 3).
- Produces:
  - `record ContentFile(string Path, string Text)` ; `record ContentPack(IReadOnlyList<ContentFile> Files, string? Migrations, IReadOnlyDictionary<string, string> Texts)`.
  - `enum ContentMigrationAction { Replace = 0, Retire = 1 }` ; `record ContentMigrationRule(string From, ContentMigrationAction Action, string? To)`.
  - `ContentTexts.Parse(string body) : IReadOnlyDictionary<string, string>` (lignes `clé=valeur`, `#` commentaire, BOM et CRLF tolérés).
  - `ContentPackValidator(IEnumerable<IContentKind> kinds)` : `ValidatedPack Validate(ContentPack pack, Func<int, bool> furnitureExists)` où `record ValidatedPack(IReadOnlyDictionary<string, ContentDefinition> Definitions, IReadOnlyList<ContentMigrationRule> Rules, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Texts, IReadOnlyList<ContentFinding> Findings)` avec `bool HasErrors`.

- [ ] **Step 1: Écrire les tests** (Review Focus 1, 2, 3 compris) :

```csharp
public sealed class ContentPackValidatorTests
{
    private static ContentPackValidator Validator() =>
        new([new ClassKind(), new SpecializationKind(), new ProgressionKind(), new ItemKind()]);

    private static ContentFile F(string path, string json) => new(path, json);

    private const string Warrior = """{ "id": "class.warrior", "kind": "class", "role": "tank", "range": 1, "primaryStat": "strength", "base": { "strength": 1, "intelligence": 1, "agility": 1, "health": 1, "mana": 1 }, "perLevel": {}, "specializations": ["spec.warrior.guardian"] }""";
    private const string Guardian = """{ "id": "spec.warrior.guardian", "kind": "specialization", "class": "class.warrior" }""";
    private static readonly Dictionary<string, string> Fr = new() { ["fr"] = "class.warrior.name=Guerrier\nclass.warrior.description=x\nspec.warrior.guardian.name=Gardien\nspec.warrior.guardian.description=x" };

    private static ValidatedPack Run(params ContentFile[] files) =>
        Validator().Validate(new ContentPack(files, null, Fr), _ => true);

    [Fact]
    public void AConsistentPack_HasNoError() => Run(F("class/warrior.json", Warrior), F("specialization/guardian.json", Guardian)).HasErrors.Should().BeFalse();

    [Fact]
    public void ABomAndCrlf_AreRead() => Run(F("class/warrior.json", "﻿" + Warrior.Replace(",", ",\r\n")), F("specialization/guardian.json", Guardian)).HasErrors.Should().BeFalse();

    [Fact]
    public void BadJson_NamesTheFile() => Run(F("class/warrior.json", "{ nope")).Findings.Should().ContainSingle(f => f.Code == "Json" && f.File == "class/warrior.json");

    [Fact]
    public void AnUnknownKind_IsAnError() => Run(F("x.json", """{ "id": "spell.fire", "kind": "spell" }""")).Findings.Select(f => f.Code).Should().Contain("UnknownKind");

    [Fact]
    public void AnIdWhosePrefixIsNotItsKind_IsAnError() => Run(F("class/w.json", Warrior.Replace("\"class.warrior\"", "\"item.warrior\"")), F("specialization/guardian.json", Guardian)).Findings.Select(f => f.Code).Should().Contain("IdKindMismatch");

    [Fact]
    public void TheSameIdTwice_NamesBothFiles()
    {
        ValidatedPack pack = Run(F("class/a.json", Warrior), F("class/b.json", Warrior), F("specialization/guardian.json", Guardian));

        pack.Findings.Should().ContainSingle(f => f.Code == "DuplicateId").Which.Message.Should().Contain("class/a.json").And.Contain("class/b.json");
    }

    [Fact]
    public void AMissingTranslation_IsAWarning() =>
        Validator().Validate(new ContentPack([F("class/warrior.json", Warrior), F("specialization/guardian.json", Guardian)], null, new Dictionary<string, string> { ["fr"] = "" }), _ => true)
            .Findings.Should().Contain(f => f.Code == "MissingText" && f.Severity == ContentSeverity.Warning);

    [Fact]
    public void NoFrenchTexts_IsAnError() =>
        Validator().Validate(new ContentPack([F("class/warrior.json", Warrior), F("specialization/guardian.json", Guardian)], null, new Dictionary<string, string>()), _ => true)
            .Findings.Select(f => f.Code).Should().Contain("NoFrenchTexts");

    [Fact]
    public void AnItemOnAnUnknownFurniture_IsAnError() =>
        Validator().Validate(new ContentPack([F("item/chair.json", """{ "id": "item.furniture.chair", "kind": "item", "category": "furniture", "rarity": "common", "furnitureDefinition": 999 }""")], null, Fr), id => id != 999)
            .Findings.Select(f => f.Code).Should().Contain("UnknownFurniture");

    [Fact]
    public void AMigrationToAnotherKind_IsAnError() =>
        Validator().Validate(new ContentPack([F("class/warrior.json", Warrior), F("specialization/guardian.json", Guardian)], """[{ "from": "item.old", "action": "replace", "to": "class.warrior" }]""", Fr), _ => true)
            .Findings.Select(f => f.Code).Should().Contain("MigrationKindMismatch");
}
```

Ordre de la validation (spec §9) : (1) chaque fichier : BOM retiré, kind lu par `JsonDocument` (champ `kind` absent → `NoKind`), sorte inconnue → `UnknownKind`, `Parse` (`JsonException` → `Json` avec le message), id mal formé → `BadId`, préfixe ≠ sorte → `IdKindMismatch` ; (2) doublons → `DuplicateId` (message avec les deux chemins) ; (3) `Check` de chaque sorte sur l'ensemble ; (4) règles de migration : `From` et `To` bien formés, `Replace` sans `To` ou `To` absent du paquet → `BrokenMigration`, sortes de `From` et `To` différentes → `MigrationKindMismatch` ; (5) textes : `fr` absent → `NoFrenchTexts` (erreur), clé `<id>.name`/`<id>.description` absente de `fr` → `MissingText` (avertissement), clé de `fr` sans contenu → `UnusedText` (avertissement).

- [ ] **Step 2: Vérifier l'échec** · Expected: échec de compilation.
- [ ] **Step 3: Implémenter** `ContentParser` (un fichier → définition ou constats), `ContentTexts`, `ContentPackValidator` (assemble les étapes, ne lève jamais).
- [ ] **Step 4: Vérifier le succès** · Expected: PASS (10).
- [ ] **Step 5: Commit** — `rpg : validation d'un paquet de contenu entier`.

---

### Task 5: Les tables

**Files:**
- Create: `Vortex.Database/Entities/Rpg/RpgContentEntity.cs`, `RpgContentPackEntity.cs`, `RpgContentMigrationEntity.cs`, `RpgContentTextEntity.cs`
- Modify: `Vortex.Database/Context/VortexDbContext.cs` (4 `DbSet`)
- Regenerate: `Vortex.Database/Migrations/*InitialCreate*`
- Create: `scripts/sql/2026-10-08-rpg-content-tables.sql`

**Interfaces:**
- Produces (snake_case, `VortexEntity` pour id et horodatages) :
  - `rpg_content` : `ContentId` (`content_id`, varchar 64, index unique), `Kind` (varchar 32), `Document` (longtext), `Hash` (char 64), `PackId` (`pack_id`, FK `rpg_content_packs`).
  - `rpg_content_packs` : `AccountId` (`account_id`, int), `Added`, `Changed`, `Removed` (int). Son `id` est la version du paquet.
  - `rpg_content_migrations` : `FromContentId`, `Action` (`ContentMigrationAction`, `[DefaultValue]`, jamais renuméroté), `ToContentId` (nullable), `PackId`.
  - `rpg_content_texts` : `Language` (varchar 8, unique), `Body` (longtext).
  - DbSets : `RpgContent`, `RpgContentPacks`, `RpgContentMigrations`, `RpgContentTexts`.

- [ ] **Step 1: Écrire les entités** (copier la forme de `Vortex.Database/Entities/World/ZoneEntity.cs`).
- [ ] **Step 2: Régénérer** — `node .claude/skills/changing-the-schema/scripts/regenerate-initialcreate.mjs` · Expected: les 4 tables dans `Up()`, rien d'autre de nouveau.
- [ ] **Step 3: Base neuve** — `pwsh .claude/skills/changing-the-schema/scripts/fresh-db.ps1 -Name vortex_schema_check` · Expected: migration et import réussis.
- [ ] **Step 4: Base du propriétaire** — `scripts/sql/2026-10-08-rpg-content-tables.sql` : les `CREATE TABLE IF NOT EXISTS` des 4 tables, recopiés du SQL de la migration (`dotnet ef migrations script` sur la base neuve), puis la mise à jour de la ligne `__EFMigrationsHistory` vers l'id de la nouvelle `InitialCreate`. Re-runnable. L'appliquer à `ambreia-database` (additif, aucune donnée touchée), mot de passe par `MYSQL_PWD`.
- [ ] **Step 5: Tests et commit** — `dotnet test Vortex.Database.Tests` · Expected: PASS (dont `SeedCatalogTests`). Commit `base : tables du contenu RPG`.

---

### Task 6: Le registre

**Files:**
- Create: `Vortex.Rpg/Content/IRpgContent.cs`, `Registry/ContentSet.cs`, `Registry/RpgContentRegistry.cs`, `Registry/ContentInvalidException.cs`, `Vortex.Primitives/Events/Rpg/ContentReloadedEvent.cs`
- Test: `Vortex.Database.Tests/Rpg/RpgContentRegistryTests.cs` (SQLite, comme `ZoneEditorServiceTests`)

**Interfaces:**
- Consumes: `ContentPackValidator`, entités (Tasks 4, 5), `IFurnitureDefinitionProvider.TryGetDefinition`.
- Produces:
  - `IRpgContent` : `T? Get<T>(string id) where T : ContentDefinition`, `IReadOnlyList<T> All<T>() where T : ContentDefinition`, `long Version`.
  - `RpgContentRegistry : IRpgContent, IReferenceDataProvider` (`LoadStage => 1` : après le catalogue de meubles, étape 0). `ReloadAsync(ct)` lit `rpg_content` + `rpg_content_texts`, reconstruit un paquet et le valide avec `ContentPackValidator` ; erreur → `ContentInvalidException(findings)` dont le message liste les constats ; sinon `Volatile.Write` d'un `ContentSet` neuf, log (version, compte par sorte, durée), et `ContentReloadedEvent(version, added, changed, removed)`.
  - `record ContentReloadedEvent(long PackVersion, int Added, int Changed, int Removed) : IEvent`.

- [ ] **Step 1: Écrire les tests** :
  - `AnEmptyDatabase_GivesAnEmptyRegistry` (Review Focus 4) : `Version == 0`, `All<ClassDefinition>()` vide, pas d'exception.
  - `StoredContent_IsServedByIdAndByKind` : insérer 2 lignes valides, recharger, `Get<ClassDefinition>("class.warrior")` non nul.
  - `InvalidStoredContent_RefusesToLoad` : une ligne d'objet avec une classe inconnue → `ContentInvalidException` dont le message contient `class.paladin`.
  - `AFailedReload_KeepsThePreviousContent` : charger un contenu valide, corrompre une ligne, recharger → exception, `Get` rend toujours l'ancien.
  - `AnItemOnAFurniture_LoadsAfterTheCatalogue` (Review Focus 3) : `LoadStage` vaut 1 et le faux catalogue connaît la définition 230 → chargement sans erreur.
- [ ] **Step 2: Vérifier l'échec** · Expected: compilation.
- [ ] **Step 3: Implémenter.**
- [ ] **Step 4: Vérifier le succès** · Expected: PASS (5).
- [ ] **Step 5: Commit** — `rpg : registre du contenu rechargé à chaud`.

---

### Task 7: L'import

**Files:**
- Create: `Vortex.Primitives/Rpg/Content/ContentImportResult.cs`, `IRpgContentService.cs`, `Vortex.Rpg/Content/Migrations/IContentReferences.cs`, `Vortex.Rpg/Content/Import/RpgContentService.cs`
- Test: `Vortex.Database.Tests/Rpg/RpgContentServiceTests.cs`

**Interfaces:**
- Consumes: Tasks 4–6.
- Produces:
  - `record ContentImportResult(bool Imported, long PackVersion, int Added, int Changed, int Removed, IReadOnlyList<ContentFinding> Findings)`.
  - `IRpgContentService` : `Task<ContentImportResult> ImportAsync(ContentPack pack, int accountId, CancellationToken ct)`, `Task<string?> GetTextsAsync(string language, CancellationToken ct)`.
  - `IContentReferences` : `string Kind`, `Task<bool> IsReferencedAsync(VortexDbContext db, string contentId, CancellationToken ct)`, `Task ReplaceAsync(VortexDbContext db, string from, string to, CancellationToken ct)`. Aucune implémentation dans ce plan (les specs Personnage et Objets en apportent) : sans implémentation pour une sorte, rien n'est référencé.

Déroulé (spec §6) : valider → erreurs : `Imported = false`, rien écrit. Comparer par `Hash` (SHA-256 du document re-sérialisé par `ContentJson`) avec `rpg_content` : aucun changement et mêmes textes → `Imported = true`, version inchangée, aucune écriture. Retraits : pour chacun, si une `IContentReferences` de sa sorte le dit référencé et qu'aucune règle `Replace` du paquet ne le couvre → erreur `ReferencedWithoutMigration`. Écrire en **une transaction** (`Database.BeginTransactionAsync`) : nouvelle ligne `rpg_content_packs`, ajouts, modifications, retraits (suppression douce), règles `Replace` non encore appliquées (`ReplaceAsync` + ligne `rpg_content_migrations`), textes ; commit ; puis `registry.ReloadAsync`.

- [ ] **Step 1: Écrire les tests** :
  - `AnInvalidPack_WritesNothing` : aucune ligne, registre intact.
  - `AValidPack_IsWrittenAndServed` : lignes écrites, `PackVersion == 1`, `registry.Get` les sert.
  - `TheSamePackTwice_WritesNothingTheSecondTime` : seconde réponse `PackVersion == 1`, une seule ligne `rpg_content_packs`.
  - `AChangedDocument_IsUpdated` / `AFileRemoved_IsRetired`.
  - `RemovingAReferencedId_WithoutARule_IsRefused` : fausse `IContentReferences` qui dit « référencé » → `ReferencedWithoutMigration`, rien écrit.
  - `AReplaceRule_RewritesTheReferences_Once` : la fausse `ReplaceAsync` est appelée une fois ; ré-import identique → zéro appel.
  - `AFailureWhileWriting_RollsEverythingBack` (Review Focus 5) : fausse `IContentReferences.ReplaceAsync` qui lève → aucune ligne, version 0, registre intact.
  - `TheTextsAreKeptPerLanguage` : `GetTextsAsync("fr")` rend le corps importé.
  - `TwoImportsAtOnce_AreSerialized` (spec §11) : deux `ImportAsync` lancés ensemble avec deux paquets différents → deux versions (1 puis 2), le second validé contre l'état écrit par le premier. Le service sérialise ses imports (`SemaphoreSlim(1)` : c'est un service du silo, pas un grain).
  - Chaque import réussi écrit un log structuré (spec §12) : compte, version, ajouts, modifications, retraits, règles appliquées, durée.
- [ ] **Step 2: Vérifier l'échec** · Expected: compilation.
- [ ] **Step 3: Implémenter.**
- [ ] **Step 4: Vérifier le succès** · Expected: PASS (9).
- [ ] **Step 5: Commit** — `rpg : import du contenu tout ou rien, avec migrations`.

---

### Task 8: Module, routes, capability

**Files:**
- Create: `Vortex.Rpg/RpgModule.cs`, `Vortex.Rpg/RpgConfig.cs`, `Vortex.WebApi/Hosting/WebApiEndpoints.Content.cs`
- Modify: `Vortex.Main/Program.cs` (`AddHostPlugin<RpgModule>` après `WorldModule`), `Vortex.Main/Vortex.Main.csproj` (référence), `Vortex.Primitives/Permissions/Capabilities.cs` (classe `Content` : `Publish = "content.publish"`, ajoutée à `All`), `Vortex.WebApi/WebApiPlugin.cs` (`UseHostService<IRpgContentService>()`), `Vortex.WebApi/Hosting/WebApiWebHost.cs` (`ForwardSingletons`), `Vortex.WebApi/Hosting/WebApiEndpoints.cs` (`MapContent(app)`), `Vortex.WebApi.Tests/WebApiTestFactory.cs` (faux service), `Vortex.WebApi/openapi.json` (`UPDATE_OPENAPI=1`), `scripts/hooks/authorization-surface-baseline.json` (`node scripts/hooks/check-authorization-surface.mjs --update`)
- Test: `Vortex.WebApi.Tests/ContentEndpointsTests.cs`

**Interfaces:**
- `RpgModule.ConfigureServices` : `AddSingleton<IContentKind, ClassKind>()` (et les trois autres), `AddSingleton<ContentPackValidator>()`, `AddSingleton<RpgContentRegistry>()`, `AddSingleton<IRpgContent>(sp => sp.GetRequiredService<RpgContentRegistry>())`, `AddSingleton<IReferenceDataProvider>(sp => sp.GetRequiredService<RpgContentRegistry>())`, `AddSingleton<IRpgContentService, RpgContentService>()`.
- `POST /api/content/import` (corps `ContentPack`, 16 Mo max, `content.publish`) → 200 avec `ContentImportResult` si `Imported`, 400 avec le même corps sinon ; 401 / 403 comme l'éditeur (`DenyAsync`).
- `GET /api/public/content/texts/{language}` → 200 `text/plain; charset=utf-8`, 404 si absent.

- [ ] **Step 1: Écrire les tests** : 401 sans session, 403 sans la capability, 200 et 400 relayés du faux service, textes 200 / 404.
- [ ] **Step 2–4:** échec, implémentation, succès · Expected: PASS (6). Puis `node scripts/hooks/check-webapi-forwarding.mjs` · Expected: OK.
- [ ] **Step 5: Commit** — `rpg : routes d'import et de textes du contenu`.

---

### Task 9: Schémas, paquet de démonstration, script d'import

**Files:**
- Create: `Vortex.Hosting.Tests/Rpg/ContentSchemaTests.cs`, `Vortex.Hosting.Tests/Rpg/ContentPackInRepoTests.cs`, `schemas/content/{class,specialization,progression,item}.schema.json`
- Create: `content/class/{warrior,archer,mage,cleric}.json`, `content/specialization/{warrior_guardian,archer_marksman,mage_elementalist,cleric_priest}.json`, `content/progression/default.json`, `content/item/*.json` (une dizaine), `content/texts/fr.txt`, `content/migrations.json` (`[]`)
- Create: `scripts/content/push.mjs`
- Modify: `Vortex.Hosting.Tests/Vortex.Hosting.Tests.csproj` (référence `Vortex.Rpg`)

**Interfaces:**
- `ContentSchemaTests` : comme `ZoneSchemaTests`, pour chaque sorte (`UPDATE_CONTENT_SCHEMAS=1` les réécrit).
- `ContentPackInRepoTests.TheRepositoryPack_IsValid` : lit `content/` comme le script, le valide avec les 4 sortes et un faux catalogue qui connaît les meubles référencés → aucune erreur (§20 « cohérence » de la spec unifiée).
- `push.mjs` : `node scripts/content/push.mjs [--base http://127.0.0.1:8080] [--email … --password-env VAR]` ; lit `content/**/*.json` (chemins relatifs à `content/`), `migrations.json`, `texts/*.txt` ; se connecte (`/api/public/authentication/login`, mot de passe lu dans une variable d'environnement, jamais affiché) ; `POST /api/content/import` ; affiche ajouts / modifications / retraits et chaque constat `[Error|Warning] code fichier id : message` ; code de sortie 1 si `Imported == false`.

Contenu : les 4 classes de §5.3 de la spec unifiée (Guerrier FOR 1 case menace × 3, Archer AGI 6, Mage INT 5, Clerc INT 5) avec des valeurs de `base` / `perLevel` équilibrées (PV 120 / 90 / 80 / 100 au niveau 1), la courbe `progression.default` (19 valeurs croissantes, 100 au niveau 1 → 2), des objets couvrant chaque emplacement (au moins un par classe), un matériau empilable (`maxStack` 50), un objet de quête (`on_pickup`), un meuble (`furnitureDefinition` 230, le trône), un objet `rare` lié à l'équipement ; `fr.txt` complet.

- [ ] **Step 1:** écrire les deux tests de `Vortex.Hosting.Tests/Rpg` · **Step 2:** échec · **Step 3:** générer les schémas (`UPDATE_CONTENT_SCHEMAS=1 dotnet test Vortex.Hosting.Tests --filter ContentSchemaTests`), écrire `content/` et `push.mjs` · **Step 4:** `dotnet test Vortex.Hosting.Tests --filter "FullyQualifiedName~Rpg"` · Expected: PASS.
- [ ] **Step 5: Commit** — `rpg : schémas, contenu de démonstration et script d'import`.

---

### Task 10: Preuve de bout en bout et documentation

**Files:**
- Modify: `docs/architecture.md` (une section « Contenu RPG »), `AGENTS.md` (ligne « Contenu RPG » dans *Where code goes*), `.claude/skills/run-vortex-emulator/SKILL.md` (comment pousser le contenu)

- [ ] **Step 1: Gate** — `dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudQualityGate` (redirigé vers un fichier) · Expected: 0 erreur, tous les tests verts.
- [ ] **Step 2: En vrai** — démarrer l'émulateur sur `ambreia-database` (driver), puis `node scripts/content/push.mjs` · Expected: « 20+ ajoutés », code 0. Relancer : « aucun changement ».
- [ ] **Step 3: Refus** — introduire une faute dans une copie de `content/` (classe autorisée `class.paladin`), pousser cette copie · Expected: code 1, constat qui nomme le fichier ; la base n'a pas changé (re-pousser l'original : « aucun changement »).
- [ ] **Step 4: Redémarrage** — arrêter, redémarrer l'émulateur · Expected: le log du registre annonce la version et le nombre de définitions par sorte.
- [ ] **Step 5: Docs et commit** — `docs : pipeline de contenu RPG`.
