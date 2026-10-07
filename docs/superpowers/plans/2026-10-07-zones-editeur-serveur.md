# Éditeur de cartes, spec 1 (format de zone et serveur) — plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Une zone d'Ambreia est un document JSON versionné en base ; l'API web le valide, l'enregistre, le publie et le verrouille ; une room-zone tire sa carte et son décor du document publié et se recharge à chaque publication.

**Architecture:** Les records du document vivent dans `Vortex.Primitives/World` (partagés par Rooms et le plugin web) et leur JSON Schema est exporté dans `schemas/zone.schema.json`. Un nouveau projet Platform, `Vortex.World`, possède les tables `zones`/`zone_versions`, la validation, la publication et le verrou ; il expose `IZoneEditorService` (au plugin web) et `IZoneRoomSource` (à `RoomGrain`). `RoomGrain` compile `floor` en modèle Habbo avec le compilateur existant et construit le décor en mémoire, sans ligne `furniture`.

**Tech Stack:** .NET 10, System.Text.Json (+ `JsonSchemaExporter`, aucun paquet ajouté), EF Core 9 + Pomelo, Orleans, ASP.NET minimal API (plugin web), xUnit + FluentAssertions, SQLite pour les tests de stockage.

**Spec:** `docs/superpowers/specs/2026-10-07-rpg-world-map-editor-design.md`

## Global Constraints

- Une zone ≤ 64 × 64 cases ; hauteur de case entière 0–32 (alphabet base 33 du modèle Habbo, `x` = trou).
- Un meuble est désigné par **l'id de sa définition** (`furniture_definitions.id`, égal au `sprite_id` Habbo, stable d'un import à l'autre) **et** son nom de classe (`name`) ; l'id fait foi, un nom différent est un avertissement. Mesuré le 2026-10-07 : 55 282 définitions, 51 352 noms distincts — le nom seul est ambigu.
- Rotations Habbo : 0, 2, 4, 6 (`Rotation.North/East/South/West`).
- `Vortex.World` est en couche **Platform** : ajouté à `PLATFORM` dans `scripts/hooks/check-architecture-walls.mjs`.
- Le document est sérialisé en **camelCase**, énumérations en chaînes, via `ZoneJson.Options` partout (serveur, tests, export du schéma).
- Une erreur de validation refuse l'enregistrement ; un avertissement ne le refuse pas. Références vers le contenu RPG (PNJ, monstres, loot, quêtes…) = **avertissement** `UnresolvedReference` tant que leur système n'existe pas.
- Capabilities : `world.zone.edit`, `world.zone.publish` (rôle `owner`).
- Réglage `Vortex:World:PublishOnSave` (bool, défaut `false`; `true` dans le développement via `appsettings.Development.json` du propriétaire) ; `Vortex:World:LockMinutes` (défaut 30) ; `Vortex:World:DecorBudget` (défaut 2000).
- Persistance d'une room-zone : **aucune écriture** dans `furniture` (le décor n'a pas de lignes ; un id de décor peut égaler l'id d'une ligne d'une autre room).
- Commits : sur `main`, chemins explicites (`git commit -o …`), messages en français.

## Review Focus

1. **Décor qui se croit modifié** (une logique change un état) → la room ne doit jamais écrire `furniture` : un id de décor 12 écraserait la ligne 12 d'une autre room. Test : Task 5, `AZone_NeverFlushesItsDecor`.
2. **Document d'un ancien schéma ou JSON invalide envoyé par l'éditeur** → 400 avec un constat lisible (`code`, `message`), jamais un 500. Test : Task 7, `SavingGarbage_IsABadRequestWithFindings`.
3. **Deux constructeurs sur une même zone** → le second ne peut pas enregistrer tant que le premier tient le verrou, sauf à le forcer avec `world.zone.publish`. Test : Task 4, `AnotherAccount_CannotSaveWhileTheLockIsHeld`.
4. **Publication pendant que des joueurs sont dans la zone** → chacun reçoit `RoomForward` vers la même room et la rejoint par la porte de la nouvelle version ; personne ne reste dans l'ancienne activation. Test : Task 6, `ReloadingAnOccupiedZone_ForwardsEveryOccupant`.
5. **Meuble dont la définition a disparu au réimport** → la zone publiée se charge quand même, sans cet objet (log). Test : Task 5, `ADecorObjectWithAnUnknownDefinition_IsLeftOut`.

---

## Fichiers

| Fichier | Rôle |
|---|---|
| `Vortex.Primitives/World/ZoneDocument.cs` | records du document (tous les calques) |
| `Vortex.Primitives/World/ZoneRules.cs` | conditions et actions (polymorphes sur `kind`) |
| `Vortex.Primitives/World/ZoneJson.cs` | options JSON uniques |
| `Vortex.Primitives/World/ZoneFinding.cs` | constat de validation |
| `Vortex.Primitives/World/IZoneRoomSource.cs` | ce que `RoomGrain` demande |
| `Vortex.Primitives/World/IZoneEditorService.cs` | ce que le plugin web appelle |
| `schemas/zone.schema.json` | JSON Schema exporté (commité) |
| `Vortex.World/Vortex.World.csproj`, `WorldModule.cs`, `WorldConfig.cs` | projet, enregistrement, réglages |
| `Vortex.World/ZoneValidator.cs` | règles de la spec §5 |
| `Vortex.World/ZoneStore.cs` | tables `zones` / `zone_versions` |
| `Vortex.World/ZoneEditorService.cs` | enregistrer, publier, revenir, verrou, palette |
| `Vortex.World/ZoneRoomSource.cs` | document publié d'une room |
| `Vortex.Database/Entities/World/ZoneEntity.cs`, `ZoneVersionEntity.cs` | tables |
| `Vortex.Rooms/Grains/Zones/ZoneRoomLoader.cs` | document → modèle Habbo + décor |
| `Vortex.Rooms/Grains/RoomGrain.cs`, `RoomGrain.Furni.cs`, `Modules/RoomFurniModule.cs` | branchement zone |
| `Vortex.Primitives/Rooms/Grains/IRoomCore.cs` | `ReloadZoneAsync` |
| `Vortex.WebApi/Hosting/WebApiEndpoints.Editor.cs` | routes `/api/editor/*` |
| `Vortex.Primitives/Permissions/Capabilities.cs`, `Vortex.Permissions/DefaultRoles.cs` | droits |

---

### Task 1: Le document de zone, son JSON et son schéma

**Files:**
- Create: `Vortex.Primitives/World/ZoneDocument.cs`, `Vortex.Primitives/World/ZoneRules.cs`, `Vortex.Primitives/World/ZoneJson.cs`, `Vortex.Primitives/World/ZoneFinding.cs`
- Create: `schemas/zone.schema.json` (généré par le test)
- Test: `Vortex.Hosting.Tests/World/ZoneSchemaTests.cs`

**Interfaces:**
- Produces: `ZoneDocument` et ses calques (noms ci-dessous), `ZoneJson.Options`, `ZoneJson.Serialize(ZoneDocument)`, `ZoneJson.Deserialize(string) : ZoneDocument`, `ZoneFinding(ZoneSeverity Severity, string Code, string? ElementId, string Message)`, `ZoneDocument.CurrentSchemaVersion = 1`.

- [ ] **Step 1: Écrire les records**

`Vortex.Primitives/World/ZoneDocument.cs` :

```csharp
using System.Collections.Generic;

namespace Vortex.Primitives.World;

/// <summary>
/// A zone as the map editor builds it and the server runs it: every layer of the editor, the RPG
/// ones included, so a zone built before a system exists stays valid once it does.
/// </summary>
/// <remarks>Spec: docs/superpowers/specs/2026-10-07-rpg-world-map-editor-design.md §4.</remarks>
public sealed record ZoneDocument
{
    public const int CurrentSchemaVersion = 1;
    public const int MaxSize = 64;
    public const int MaxTileHeight = 32;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public required ZoneSettings Settings { get; init; }
    public required ZoneFloor Floor { get; init; }
    public ZoneWalls Walls { get; init; } = new();
    public List<ZoneDecorObject> Decor { get; init; } = [];
    public List<ZoneWallDecorObject> WallDecor { get; init; } = [];
    public List<ZoneEntry> Entries { get; init; } = [];
    public List<ZonePassage> Passages { get; init; } = [];
    public List<ZoneArea> Zones { get; init; } = [];
    public List<ZoneNpc> Npcs { get; init; } = [];
    public List<ZoneSpawn> Spawns { get; init; } = [];
    public List<ZoneResource> Resources { get; init; } = [];
    public List<ZoneChest> Chests { get; init; } = [];
    public List<ZoneInteractive> Interactives { get; init; } = [];
    public List<ZoneBoss> Bosses { get; init; } = [];
    public List<ZoneEvent> Events { get; init; } = [];
    public List<ZoneTrigger> Triggers { get; init; } = [];
    public ZoneAmbience Ambience { get; init; } = new();
    public List<ZonePrefabUse> Prefabs { get; init; } = [];
}

public enum ZoneType { World, Dungeon, Lobby, Housing, Guild, Arena }

public sealed record ZoneSettings
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Region { get; init; } = string.Empty;
    public ZoneType Type { get; init; } = ZoneType.World;
    public int Capacity { get; init; } = 50;
    public int RecommendedLevel { get; init; } = 1;
    public bool PvpAllowed { get; init; }
    public string Music { get; init; } = string.Empty;
    public string Weather { get; init; } = string.Empty;
    public bool Template { get; init; }
}

public readonly record struct ZoneCell(int X, int Y);

public sealed record ZoneFloor
{
    public required int Width { get; init; }
    public required int Length { get; init; }
    /// <summary>Row-major, <c>Width × Length</c> cells; index = y * Width + x.</summary>
    public required List<ZoneTile> Tiles { get; init; }
    public required ZoneCell Door { get; init; }
    public int DoorDirection { get; init; } = 2;
}

public sealed record ZoneTile
{
    public int Height { get; init; }
    public bool Walkable { get; init; } = true;
    public bool Hole { get; init; }
    public string? Zone { get; init; }
    public string? Note { get; init; }
}

public sealed record ZoneWalls
{
    public bool Visible { get; init; } = true;
    public int Height { get; init; }
    public int Thickness { get; init; }
}

public sealed record ZoneCollision
{
    public bool? Walkable { get; init; }
    public bool? BlocksSight { get; init; }
    public double? Height { get; init; }
    public bool? Seat { get; init; }
}

public sealed record ZoneDecorObject
{
    public required string Id { get; init; }
    public required int Definition { get; init; }
    public required string Class { get; init; }
    public required int X { get; init; }
    public required int Y { get; init; }
    public double Z { get; init; }
    public int Rotation { get; init; }
    public int State { get; init; }
    public ZoneCollision? Collision { get; init; }
    public string? Interaction { get; init; }
    public string? Tag { get; init; }
    public bool Locked { get; init; }
}

public enum ZoneWallSide { Left, Right }

public sealed record ZoneWallDecorObject
{
    public required string Id { get; init; }
    public required int Definition { get; init; }
    public required string Class { get; init; }
    public required ZoneWallSide Side { get; init; }
    public required int X { get; init; }
    public required int Y { get; init; }
    public int OffsetX { get; init; }
    public int OffsetY { get; init; }
    public int State { get; init; }
}

public sealed record ZoneEntry
{
    public required string Id { get; init; }
    public required string Kind { get; init; }
    public required ZoneCell Cell { get; init; }
    public int Direction { get; init; } = 2;
}

public sealed record ZoneArea
{
    public required string Id { get; init; }
    public required string Kind { get; init; }
    public string Name { get; init; } = string.Empty;
    public required List<ZoneCell> Polygon { get; init; }
    public int Priority { get; init; }
    public string Music { get; init; } = string.Empty;
    public int RecommendedLevel { get; init; }
    public List<string> Tags { get; init; } = [];
    public List<ZoneCondition> Conditions { get; init; } = [];
}

public sealed record ZonePassage
{
    public required string Id { get; init; }
    public required List<ZoneCell> Area { get; init; }
    public required string DestinationZone { get; init; }
    public required string DestinationEntry { get; init; }
    public bool Fade { get; init; } = true;
    public int Cost { get; init; }
    public int CooldownSeconds { get; init; }
    public List<ZoneCondition> Conditions { get; init; } = [];
}

public sealed record ZoneNpc
{
    public required string Id { get; init; }
    public required string Template { get; init; }
    public string? Name { get; init; }
    public string? Look { get; init; }
    public required ZoneCell Cell { get; init; }
    public int Rotation { get; init; } = 2;
    public List<string> Interactions { get; init; } = [];
    public string? Animation { get; init; }
    public string? Icon { get; init; }
    public List<ZoneCondition> AppearWhen { get; init; } = [];
}

public sealed record ZoneSpawn
{
    public required string Id { get; init; }
    public required List<ZoneCell> Area { get; init; }
    public required string Monster { get; init; }
    public int Min { get; init; } = 1;
    public int Max { get; init; } = 1;
    public int RespawnSeconds { get; init; } = 30;
    public int WanderRadius { get; init; } = 3;
    public int AggroRadius { get; init; } = 5;
    public int LevelMin { get; init; } = 1;
    public int LevelMax { get; init; } = 1;
    public bool AoiActivation { get; init; } = true;
    public string? Group { get; init; }
    public List<ZoneCondition> Conditions { get; init; } = [];
}

public sealed record ZoneResource
{
    public required string Id { get; init; }
    public string? Decor { get; init; }
    public ZoneCell? Cell { get; init; }
    public required string Template { get; init; }
    public string? Profession { get; init; }
    public int Level { get; init; }
    public int RespawnSeconds { get; init; } = 60;
}

public sealed record ZoneChest
{
    public required string Id { get; init; }
    public required string Decor { get; init; }
    public required string LootTable { get; init; }
    public int RespawnSeconds { get; init; }
    public bool PerPlayer { get; init; }
    public List<ZoneCondition> Conditions { get; init; } = [];
}

public sealed record ZoneInteractive
{
    public required string Id { get; init; }
    public required string Decor { get; init; }
    public required string Interaction { get; init; }
}

public sealed record ZoneBoss
{
    public required string Id { get; init; }
    public required List<ZoneCell> Arena { get; init; }
    public required string Encounter { get; init; }
    public required ZoneCell Spawn { get; init; }
    public List<ZoneCell> Adds { get; init; } = [];
    public List<string> Doors { get; init; } = [];
}

public sealed record ZoneEvent
{
    public required string Id { get; init; }
    public required List<ZoneCell> Area { get; init; }
    public required string Definition { get; init; }
    public int DurationSeconds { get; init; }
    public int CooldownSeconds { get; init; }
}

public enum ZoneTriggerType
{
    EnterZone, LeaveZone, Interact, MonsterKilled, Timer, QuestState, PlayerLevel, ItemOwned,
}

public sealed record ZoneTrigger
{
    public required string Id { get; init; }
    public required ZoneTriggerType Type { get; init; }
    public string? Target { get; init; }
    public List<ZoneCondition> Conditions { get; init; } = [];
    public List<ZoneAction> Actions { get; init; } = [];
    public int CooldownMs { get; init; }
    public bool Active { get; init; } = true;
    public bool VisibleInDebug { get; init; } = true;
}

public sealed record ZoneAmbience
{
    public string Tint { get; init; } = "#FFFFFF";
    public int Brightness { get; init; } = 100;
    public bool DayNight { get; init; }
    public List<ZoneLight> Lights { get; init; } = [];
    public List<ZoneSound> Sounds { get; init; } = [];
    public List<ZoneEffect> Effects { get; init; } = [];
}

public sealed record ZoneLight(string Id, ZoneCell Cell, string Color, int Radius);

public sealed record ZoneSound(string Id, ZoneCell Cell, string Sound, int Radius, int Volume, bool Loop);

public sealed record ZoneEffect(string Id, ZoneCell Cell, string Effect, double Scale, int Radius);

public sealed record ZonePrefabUse(string Prefab, int Version);
```

- [ ] **Step 2: Conditions, actions, options JSON, constats**

`Vortex.Primitives/World/ZoneRules.cs` :

```csharp
using System.Text.Json.Serialization;

namespace Vortex.Primitives.World;

/// <summary>One typed condition; <c>kind</c> picks the shape. An unknown kind fails to parse.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(PlayerLevelCondition), "PlayerLevel")]
[JsonDerivedType(typeof(QuestStateCondition), "QuestState")]
[JsonDerivedType(typeof(TimeOfDayCondition), "TimeOfDay")]
[JsonDerivedType(typeof(HasItemCondition), "HasItem")]
[JsonDerivedType(typeof(FactionCondition), "Faction")]
[JsonDerivedType(typeof(ZoneActiveCondition), "ZoneActive")]
public abstract record ZoneCondition;

public sealed record PlayerLevelCondition(int Min, int Max) : ZoneCondition;
public sealed record QuestStateCondition(string Quest, string State) : ZoneCondition;
public sealed record TimeOfDayCondition(int From, int To) : ZoneCondition;
public sealed record HasItemCondition(string Item, int Count) : ZoneCondition;
public sealed record FactionCondition(string Faction) : ZoneCondition;
public sealed record ZoneActiveCondition(string Zone) : ZoneCondition;

/// <summary>One typed action; <c>kind</c> picks the shape. An unknown kind fails to parse.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(StartQuestAction), "StartQuest")]
[JsonDerivedType(typeof(ShowDialogueAction), "ShowDialogue")]
[JsonDerivedType(typeof(ActivateZoneAction), "ActivateZone")]
[JsonDerivedType(typeof(DeactivateZoneAction), "DeactivateZone")]
[JsonDerivedType(typeof(TeleportAction), "Teleport")]
[JsonDerivedType(typeof(SpawnGroupAction), "SpawnGroup")]
[JsonDerivedType(typeof(PlaySoundAction), "PlaySound")]
[JsonDerivedType(typeof(OpenDoorAction), "OpenDoor")]
public abstract record ZoneAction;

public sealed record StartQuestAction(string Quest) : ZoneAction;
public sealed record ShowDialogueAction(string Dialogue) : ZoneAction;
public sealed record ActivateZoneAction(string Zone) : ZoneAction;
public sealed record DeactivateZoneAction(string Zone) : ZoneAction;
public sealed record TeleportAction(string Zone, string Entry) : ZoneAction;
public sealed record SpawnGroupAction(string Spawn) : ZoneAction;
public sealed record PlaySoundAction(string Sound) : ZoneAction;
public sealed record OpenDoorAction(string Decor) : ZoneAction;
```

`Vortex.Primitives/World/ZoneJson.cs` :

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vortex.Primitives.World;

/// <summary>The one JSON shape of a zone document, shared by the server, the tests and the schema.</summary>
public static class ZoneJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = false,
    };

    public static string Serialize(ZoneDocument document) =>
        JsonSerializer.Serialize(document, Options);

    /// <exception cref="JsonException">Malformed JSON, a missing required field, an unknown
    /// <c>kind</c> or an unknown property.</exception>
    public static ZoneDocument Deserialize(string json) =>
        JsonSerializer.Deserialize<ZoneDocument>(json, Options)
        ?? throw new JsonException("The document is null.");
}
```

`Vortex.Primitives/World/ZoneFinding.cs` :

```csharp
namespace Vortex.Primitives.World;

public enum ZoneSeverity { Error, Warning }

/// <summary>One validation result; <paramref name="ElementId"/> lets the editor centre on it.</summary>
public sealed record ZoneFinding(ZoneSeverity Severity, string Code, string? ElementId, string Message);
```

- [ ] **Step 3: Écrire le test du schéma et de l'aller-retour**

`Vortex.Hosting.Tests/World/ZoneSchemaTests.cs` :

```csharp
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Schema;
using FluentAssertions;
using Vortex.Hosting.Tests.Architecture;
using Vortex.Primitives.World;
using Xunit;

namespace Vortex.Hosting.Tests.World;

/// <summary>
/// The editor generates its TypeScript types from schemas/zone.schema.json; this keeps that file
/// equal to the C# records. UPDATE_ZONE_SCHEMA=1 rewrites it.
/// </summary>
public sealed class ZoneSchemaTests
{
    [Fact]
    public void TheCommittedSchema_MatchesTheRecords()
    {
        string exported = JsonSchemaExporter
            .GetJsonSchemaAsNode(ZoneJson.Options, typeof(ZoneDocument))
            .ToJsonString(new JsonSerializerOptions { WriteIndented = true })
            .ReplaceLineEndings("\n");
        string path = Path.Combine(RepositoryPaths.Root(), "schemas", "zone.schema.json");

        if (Environment.GetEnvironmentVariable("UPDATE_ZONE_SCHEMA") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, exported + "\n");

            return;
        }

        File.Exists(path).Should().BeTrue("run UPDATE_ZONE_SCHEMA=1 to write it");
        File.ReadAllText(path).ReplaceLineEndings("\n").TrimEnd().Should().Be(exported);
    }

    [Fact]
    public void ADocumentWithEveryLayer_SurvivesARoundTrip()
    {
        ZoneDocument document = ZoneSamples.EveryLayer();

        string json = ZoneJson.Serialize(document);

        ZoneJson.Serialize(ZoneJson.Deserialize(json)).Should().Be(json);
        json.Should().Contain("\"kind\":\"PlayerLevel\"").And.Contain("\"type\":\"World\"");
    }

    [Fact]
    public void AnUnknownConditionKind_DoesNotParse()
    {
        string json = ZoneJson.Serialize(ZoneSamples.EveryLayer())
            .Replace("\"kind\":\"PlayerLevel\"", "\"kind\":\"Telepathy\"");

        Action parse = () => ZoneJson.Deserialize(json);

        parse.Should().Throw<JsonException>();
    }
}
```

et `ZoneSamples` partagé par les tâches suivantes, `Vortex.Tests.Support/World/ZoneSamples.cs` (ajouter `<ProjectReference Include="..\Vortex.Primitives\Vortex.Primitives.csproj" />` si absent) :

```csharp
using System.Linq;
using Vortex.Primitives.World;

namespace Vortex.Tests.Support;

/// <summary>Small valid zone documents for tests: a 6 × 6 open floor, door at (0, 2).</summary>
public static class ZoneSamples
{
    public const int Throne = 230;

    public static ZoneDocument Minimal(string id = "village") =>
        new()
        {
            Settings = new ZoneSettings { Id = id, Name = "Village" },
            Floor = new ZoneFloor
            {
                Width = 6,
                Length = 6,
                Tiles = [.. Enumerable.Range(0, 36).Select(_ => new ZoneTile())],
                Door = new ZoneCell(0, 2),
            },
        };

    public static ZoneDocument EveryLayer() =>
        Minimal() with
        {
            Decor = [new ZoneDecorObject { Id = "throne_1", Definition = Throne, Class = "throne", X = 3, Y = 3 }],
            Entries = [new ZoneEntry { Id = "arrival", Kind = "arrival", Cell = new ZoneCell(0, 2) }],
            Zones = [new ZoneArea { Id = "z_safe", Kind = "Safe", Polygon = [new(0, 0), new(5, 0), new(5, 5), new(0, 5)] }],
            Spawns = [new ZoneSpawn { Id = "spawn_1", Area = [new(4, 4), new(5, 4), new(5, 5), new(4, 5)], Monster = "wolf" }],
            Npcs = [new ZoneNpc { Id = "npc_1", Template = "blacksmith", Cell = new ZoneCell(1, 1) }],
            Triggers =
            [
                new ZoneTrigger
                {
                    Id = "t_enter",
                    Type = ZoneTriggerType.EnterZone,
                    Target = "z_safe",
                    Conditions = [new PlayerLevelCondition(1, 99)],
                    Actions = [new ActivateZoneAction("z_safe")],
                },
            ],
        };
}
```

- [ ] **Step 4: Lancer pour voir l'échec, écrire le schéma, relancer**

Run: `dotnet test Vortex.Hosting.Tests --filter "FullyQualifiedName~ZoneSchema"` → `TheCommittedSchema_MatchesTheRecords` FAIL (fichier absent).
Run: `UPDATE_ZONE_SCHEMA=1 dotnet test Vortex.Hosting.Tests --filter "FullyQualifiedName~TheCommittedSchema"` puis sans la variable → PASS (3 tests). Lire `schemas/zone.schema.json` : `kind` apparaît en `anyOf`.

- [ ] **Step 5: Commit**

```bash
git add Vortex.Primitives/World Vortex.Tests.Support/World schemas/zone.schema.json Vortex.Hosting.Tests/World
git commit -o Vortex.Primitives/World Vortex.Tests.Support Vortex.Hosting.Tests/World schemas -m "zones: document de zone, JSON et schéma"
```

---

### Task 2: Projet `Vortex.World` et validateur

**Files:**
- Create: `Vortex.World/Vortex.World.csproj`, `Vortex.World/WorldModule.cs`, `Vortex.World/WorldConfig.cs`, `Vortex.World/ZoneValidator.cs`
- Modify: `Vortex.Cloud.sln` (`dotnet sln add Vortex.World/Vortex.World.csproj`), `Vortex.Main/Vortex.Main.csproj` (référence), `Vortex.Main/Program.cs` (`AddHostPlugin<WorldModule>`), `scripts/hooks/check-architecture-walls.mjs` (`PLATFORM`), `Vortex.Database.Tests/Vortex.Database.Tests.csproj` (référence `Vortex.World`)
- Test: `Vortex.Database.Tests/World/ZoneValidatorTests.cs`

**Interfaces:**
- Consumes: Task 1 records, `IFurnitureDefinitionProvider.TryGetDefinition(int)`.
- Produces: `ZoneValidator(IFurnitureDefinitionProvider defs, IOptions<WorldConfig> config)`, `IReadOnlyList<ZoneFinding> Validate(ZoneDocument doc, IReadOnlySet<string> knownZones)` ; `WorldConfig { bool PublishOnSave; int LockMinutes = 30; int DecorBudget = 2000; }`, section `Vortex:World`.

- [ ] **Step 1: Projet et module**

`Vortex.World/Vortex.World.csproj` (copié de `Vortex.Furniture.csproj`) :

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
    <InternalsVisibleTo Include="Vortex.Database.Tests" />
  </ItemGroup>
</Project>
```

`Vortex.World/WorldConfig.cs` :

```csharp
namespace Vortex.World;

public sealed class WorldConfig
{
    public const string SECTION_NAME = "Vortex:World";

    /// <summary>Development: every save is published (playable at once). Public server: false.</summary>
    public bool PublishOnSave { get; init; }

    public int LockMinutes { get; init; } = 30;

    /// <summary>Decor objects above which validation warns (client render cost).</summary>
    public int DecorBudget { get; init; } = 2000;
}
```

`Vortex.World/WorldModule.cs` (les services des tâches 4 et 5 s'y ajoutent) :

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Vortex.Primitives.Plugins;

namespace Vortex.World;

public sealed class WorldModule : IHostPluginModule
{
    public string Key => "vortex-world";

    public void ConfigureServices(IServiceCollection services, HostApplicationBuilder builder)
    {
        services.Configure<WorldConfig>(builder.Configuration.GetSection(WorldConfig.SECTION_NAME));
        services.AddSingleton<ZoneValidator>();
    }
}
```

Ajouter `builder.Services.AddHostPlugin<WorldModule>(builder);` après `FurnitureModule` dans `Program.cs`, la référence dans `Vortex.Main.csproj`, `'World'` dans `PLATFORM` (`check-architecture-walls.mjs:243`), et `dotnet sln Vortex.Cloud.sln add Vortex.World/Vortex.World.csproj`.

- [ ] **Step 2: Écrire les tests du validateur**

`Vortex.Database.Tests/World/ZoneValidatorTests.cs` :

```csharp
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Furniture.Snapshots;
using Vortex.Primitives.World;
using Vortex.Tests.Support;
using Vortex.World;
using Xunit;

namespace Vortex.Database.Tests.World;

public sealed class ZoneValidatorTests
{
    private static readonly HashSet<string> Known = ["village", "forest"];

    private static IReadOnlyList<ZoneFinding> Validate(ZoneDocument doc) =>
        new ZoneValidator(Definitions(), Options.Create(new WorldConfig { DecorBudget = 3 }))
            .Validate(doc, Known);

    private static string[] Errors(ZoneDocument doc) =>
        [.. Validate(doc).Where(f => f.Severity == ZoneSeverity.Error).Select(f => f.Code)];

    [Fact]
    public void TheMinimalSample_IsValid() => Errors(ZoneSamples.Minimal()).Should().BeEmpty();

    [Fact]
    public void TheFullSample_HasOnlyDeferredReferenceWarnings()
    {
        IReadOnlyList<ZoneFinding> findings = Validate(ZoneSamples.EveryLayer());

        findings.Where(f => f.Severity == ZoneSeverity.Error).Should().BeEmpty();
        findings.Select(f => f.Code).Distinct().Should().BeEquivalentTo(["UnresolvedReference"]);
    }

    [Fact]
    public void AFloorAbove64_IsAnError() =>
        Errors(ZoneSamples.Minimal() with { Floor = Floor(65, 1) }).Should().Contain("FloorTooLarge");

    [Fact]
    public void ATileCountThatDoesNotMatchTheSize_IsAnError() =>
        Errors(ZoneSamples.Minimal() with { Floor = ZoneSamples.Minimal().Floor with { Width = 5 } })
            .Should().Contain("FloorTileCount");

    [Fact]
    public void ADoorOnAHole_IsAnError()
    {
        ZoneDocument doc = ZoneSamples.Minimal();
        doc.Floor.Tiles[2 * 6 + 0] = new ZoneTile { Hole = true };

        Errors(doc).Should().Contain("DoorNotWalkable");
    }

    [Fact]
    public void DuplicateIds_AreAnError() =>
        Errors(ZoneSamples.EveryLayer() with
        {
            Npcs = [new ZoneNpc { Id = "throne_1", Template = "x", Cell = new ZoneCell(1, 1) }],
        }).Should().Contain("DuplicateId");

    [Fact]
    public void AnUnknownFurnitureDefinition_IsAnError() =>
        Errors(ZoneSamples.Minimal() with
        {
            Decor = [new ZoneDecorObject { Id = "d", Definition = 999, Class = "nope", X = 1, Y = 1 }],
        }).Should().Contain("UnknownFurniture");

    [Fact]
    public void ARotationTheFurnitureCannotTake_IsAnError() =>
        Errors(ZoneSamples.Minimal() with
        {
            Decor = [new ZoneDecorObject { Id = "d", Definition = ZoneSamples.Throne, Class = "throne", X = 1, Y = 1, Rotation = 3 }],
        }).Should().Contain("BadRotation");

    [Fact]
    public void AFootprintOverTheEdge_IsAnError() =>
        Errors(ZoneSamples.Minimal() with
        {
            Decor = [new ZoneDecorObject { Id = "d", Definition = Sofa, Class = "sofa_silo", X = 5, Y = 0 }],
        }).Should().Contain("OutOfFloor");

    [Fact]
    public void ATriggerAimingAtAMissingZone_IsAnError() =>
        Errors(ZoneSamples.EveryLayer() with
        {
            Triggers = [new ZoneTrigger { Id = "t", Type = ZoneTriggerType.EnterZone, Target = "z_nowhere" }],
        }).Should().Contain("BrokenReference");

    [Fact]
    public void APassageToAnUnknownZone_IsAnError() =>
        Errors(ZoneSamples.Minimal() with
        {
            Passages = [new ZonePassage { Id = "p", Area = [new(5, 5)], DestinationZone = "moon", DestinationEntry = "arrival" }],
        }).Should().Contain("UnknownZone");

    [Fact]
    public void ASpawnWalledOffFromTheDoor_IsAWarning()
    {
        ZoneDocument doc = ZoneSamples.EveryLayer();

        // A full-height wall of holes on column 3 cuts (4..5, *) off from the door at (0, 2).
        for (int y = 0; y < 6; y++)
        {
            doc.Floor.Tiles[y * 6 + 3] = new ZoneTile { Hole = true };
        }

        Validate(doc).Should().Contain(f => f.Code == "Unreachable" && f.ElementId == "spawn_1");
    }

    [Fact]
    public void MoreDecorThanTheBudget_IsAWarning() =>
        Validate(ZoneSamples.Minimal() with
        {
            Decor = [.. Enumerable.Range(0, 4).Select(i => new ZoneDecorObject { Id = $"d{i}", Definition = ZoneSamples.Throne, Class = "throne", X = i, Y = 0 })],
        }).Should().Contain(f => f.Code == "OverBudget" && f.Severity == ZoneSeverity.Warning);

    private const int Sofa = 28;

    private static ZoneFloor Floor(int width, int length) =>
        new()
        {
            Width = width,
            Length = length,
            Tiles = [.. Enumerable.Range(0, width * length).Select(_ => new ZoneTile())],
            Door = new ZoneCell(0, 0),
        };

    private static IFurnitureDefinitionProvider Definitions()
    {
        Dictionary<int, FurnitureDefinitionSnapshot> defs = new()
        {
            [ZoneSamples.Throne] = Definition(ZoneSamples.Throne, "throne", 1, 1),
            [Sofa] = Definition(Sofa, "sofa_silo", 2, 1),
        };

        return FakeProxy.Create<IFurnitureDefinitionProvider>(call =>
            call.Method.Name == nameof(IFurnitureDefinitionProvider.TryGetDefinition)
                ? defs.GetValueOrDefault((int)call.Args![0]!)
                : null
        );
    }

    private static FurnitureDefinitionSnapshot Definition(int id, string name, int width, int length) =>
        TestDefinitions.Floor(id, name, width, length);
}
```

`TestDefinitions.Floor` n'existe pas encore : l'ajouter à `Vortex.Tests.Support/World/ZoneSamples.cs`, en remplissant chaque propriété `required` de `FurnitureDefinitionSnapshot` (lire le record, `Vortex.Primitives/Furniture/Snapshots/FurnitureDefinitionSnapshot.cs` ; `ProductType = ProductType.Floor`, `TotalStates = 2`, `CanWalk = false`, le reste à des valeurs neutres) :

```csharp
public static class TestDefinitions
{
    public static FurnitureDefinitionSnapshot Floor(int id, string name, int width, int length) =>
        new()
        {
            Id = id,
            SpriteId = id,
            Name = name,
            ProductType = ProductType.Floor,
            LogicName = "furniture_basic",
            TotalStates = 2,
            Width = width,
            Length = length,
            CanWalk = false,
            // + every other required member of FurnitureDefinitionSnapshot, at its neutral value.
        };
}
```

- [ ] **Step 3: Lancer pour voir l'échec**

Run: `dotnet test Vortex.Database.Tests --filter "FullyQualifiedName~ZoneValidator"` → échec de compilation (`ZoneValidator` absent).

- [ ] **Step 4: Écrire le validateur**

`Vortex.World/ZoneValidator.cs` :

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Options;
using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Furniture.Snapshots;
using Vortex.Primitives.World;

namespace Vortex.World;

/// <summary>The rules of spec §5. Errors refuse a save; warnings do not.</summary>
public sealed class ZoneValidator(IFurnitureDefinitionProvider definitions, IOptions<WorldConfig> config)
{
    private static readonly int[] AllowedRotations = [0, 2, 4, 6];

    public IReadOnlyList<ZoneFinding> Validate(ZoneDocument doc, IReadOnlySet<string> knownZones)
    {
        List<ZoneFinding> f = [];
        ZoneFloor floor = doc.Floor;

        if (doc.SchemaVersion != ZoneDocument.CurrentSchemaVersion)
            f.Add(Error("SchemaVersion", null, $"Schema {doc.SchemaVersion}, server reads {ZoneDocument.CurrentSchemaVersion}."));

        if (floor.Width is < 1 or > ZoneDocument.MaxSize || floor.Length is < 1 or > ZoneDocument.MaxSize)
        {
            f.Add(Error("FloorTooLarge", null, $"Floor {floor.Width}×{floor.Length}; at most {ZoneDocument.MaxSize}×{ZoneDocument.MaxSize}."));
            return f;
        }

        if (floor.Tiles.Count != floor.Width * floor.Length)
        {
            f.Add(Error("FloorTileCount", null, $"{floor.Tiles.Count} tiles for {floor.Width}×{floor.Length}."));
            return f;
        }

        for (int i = 0; i < floor.Tiles.Count; i++)
        {
            if (floor.Tiles[i].Height is < 0 or > ZoneDocument.MaxTileHeight)
                f.Add(Error("TileHeight", null, $"Tile ({i % floor.Width},{i / floor.Width}) height {floor.Tiles[i].Height}."));
        }

        if (!Walkable(floor, floor.Door))
            f.Add(Error("DoorNotWalkable", null, $"Door {floor.Door} is not a walkable tile."));

        CheckIds(doc, f);
        CheckDecor(doc, f);
        CheckInternalReferences(doc, f);

        foreach (ZonePassage p in doc.Passages.Where(p => !knownZones.Contains(p.DestinationZone)))
            f.Add(Error("UnknownZone", p.Id, $"Passage to unknown zone '{p.DestinationZone}'."));

        CheckReachability(doc, f);
        DeferredReferences(doc, f);

        if (doc.Decor.Count + doc.WallDecor.Count > config.Value.DecorBudget)
            f.Add(Warning("OverBudget", null, $"{doc.Decor.Count + doc.WallDecor.Count} decor objects, budget {config.Value.DecorBudget}."));

        return f;
    }

    private static void CheckIds(ZoneDocument d, List<ZoneFinding> f)
    {
        IEnumerable<string> ids = d.Decor.Select(x => x.Id)
            .Concat(d.WallDecor.Select(x => x.Id)).Concat(d.Entries.Select(x => x.Id))
            .Concat(d.Passages.Select(x => x.Id)).Concat(d.Zones.Select(x => x.Id))
            .Concat(d.Npcs.Select(x => x.Id)).Concat(d.Spawns.Select(x => x.Id))
            .Concat(d.Resources.Select(x => x.Id)).Concat(d.Chests.Select(x => x.Id))
            .Concat(d.Interactives.Select(x => x.Id)).Concat(d.Bosses.Select(x => x.Id))
            .Concat(d.Events.Select(x => x.Id)).Concat(d.Triggers.Select(x => x.Id));

        foreach (string id in ids.GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key))
            f.Add(Error("DuplicateId", id, $"Id '{id}' is used more than once."));
    }

    private void CheckDecor(ZoneDocument d, List<ZoneFinding> f)
    {
        foreach (ZoneDecorObject o in d.Decor)
        {
            FurnitureDefinitionSnapshot? def = definitions.TryGetDefinition(o.Definition);

            if (def is null || def.ProductType != ProductType.Floor)
            {
                f.Add(Error("UnknownFurniture", o.Id, $"No floor furniture {o.Definition} ({o.Class})."));
                continue;
            }

            if (!string.Equals(def.Name, o.Class, StringComparison.Ordinal))
                f.Add(Warning("ClassMismatch", o.Id, $"Definition {o.Definition} is '{def.Name}', not '{o.Class}'."));

            if (!AllowedRotations.Contains(o.Rotation))
                f.Add(Error("BadRotation", o.Id, $"Rotation {o.Rotation}; allowed 0, 2, 4, 6."));

            if (o.State < 0 || (def.TotalStates > 0 && o.State >= def.TotalStates))
                f.Add(Error("BadState", o.Id, $"State {o.State}; the furniture has {def.TotalStates}."));

            (int w, int l) = o.Rotation is 2 or 6 ? (def.Length, def.Width) : (def.Width, def.Length);

            if (o.X < 0 || o.Y < 0 || o.X + w > d.Floor.Width || o.Y + l > d.Floor.Length)
                f.Add(Error("OutOfFloor", o.Id, $"Footprint {w}×{l} at ({o.X},{o.Y}) leaves the floor."));
        }

        foreach (ZoneWallDecorObject o in d.WallDecor)
        {
            FurnitureDefinitionSnapshot? def = definitions.TryGetDefinition(o.Definition);

            if (def is null || def.ProductType != ProductType.Wall)
                f.Add(Error("UnknownFurniture", o.Id, $"No wall furniture {o.Definition} ({o.Class})."));
        }
    }

    private static void CheckInternalReferences(ZoneDocument d, List<ZoneFinding> f)
    {
        HashSet<string> zones = [.. d.Zones.Select(z => z.Id)];
        HashSet<string> decor = [.. d.Decor.Select(x => x.Id).Concat(d.WallDecor.Select(x => x.Id))];
        HashSet<string> all = [.. zones, .. decor, .. d.Npcs.Select(x => x.Id), .. d.Spawns.Select(x => x.Id)];

        foreach (ZoneTrigger t in d.Triggers.Where(t => t.Target is not null && !all.Contains(t.Target)))
            f.Add(Error("BrokenReference", t.Id, $"Target '{t.Target}' does not exist."));

        foreach (ZoneTile tile in d.Floor.Tiles.Where(t => t.Zone is not null && !zones.Contains(t.Zone)))
            f.Add(Error("BrokenReference", tile.Zone, $"A tile names zone '{tile.Zone}', which does not exist."));

        foreach (ZoneChest c in d.Chests.Where(c => !decor.Contains(c.Decor)))
            f.Add(Error("BrokenReference", c.Id, $"Chest decor '{c.Decor}' does not exist."));

        foreach (ZoneInteractive i in d.Interactives.Where(i => !decor.Contains(i.Decor)))
            f.Add(Error("BrokenReference", i.Id, $"Interactive decor '{i.Decor}' does not exist."));
    }

    /// <summary>Walkable tiles connected to the door by 8-neighbour steps of at most one height.</summary>
    private static void CheckReachability(ZoneDocument d, List<ZoneFinding> f)
    {
        ZoneFloor floor = d.Floor;
        bool[] seen = new bool[floor.Tiles.Count];
        Queue<ZoneCell> open = new();

        if (Walkable(floor, floor.Door))
        {
            open.Enqueue(floor.Door);
            seen[Index(floor, floor.Door)] = true;
        }

        while (open.TryDequeue(out ZoneCell c))
        {
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                ZoneCell n = new(c.X + dx, c.Y + dy);

                if ((dx, dy) == (0, 0) || !Walkable(floor, n) || seen[Index(floor, n)]) continue;
                if (Math.Abs(floor.Tiles[Index(floor, n)].Height - floor.Tiles[Index(floor, c)].Height) > 1) continue;

                seen[Index(floor, n)] = true;
                open.Enqueue(n);
            }
        }

        bool Reached(ZoneCell cell) => Inside(floor, cell) && seen[Index(floor, cell)];

        foreach (ZoneSpawn s in d.Spawns.Where(s => !s.Area.Any(Reached)))
            f.Add(Warning("Unreachable", s.Id, "No tile of this spawn can be walked to from the door."));
        foreach (ZoneNpc n in d.Npcs.Where(n => !Reached(n.Cell)))
            f.Add(Warning("Unreachable", n.Id, "This NPC cannot be walked to from the door."));
        foreach (ZonePassage p in d.Passages.Where(p => !p.Area.Any(Reached)))
            f.Add(Warning("Unreachable", p.Id, "This passage cannot be walked to from the door."));
        foreach (ZoneResource r in d.Resources.Where(r => r.Cell is { } c && !Reached(c)))
            f.Add(Warning("Unreachable", r.Id, "This resource cannot be walked to from the door."));
    }

    /// <summary>Content the RPG systems will own; nothing resolves it yet (spec §5.3).</summary>
    private static void DeferredReferences(ZoneDocument d, List<ZoneFinding> f)
    {
        foreach (ZoneNpc n in d.Npcs) Unresolved(f, n.Id, "npc template", n.Template);
        foreach (ZoneSpawn s in d.Spawns) Unresolved(f, s.Id, "monster", s.Monster);
        foreach (ZoneResource r in d.Resources) Unresolved(f, r.Id, "resource", r.Template);
        foreach (ZoneChest c in d.Chests) Unresolved(f, c.Id, "loot table", c.LootTable);
        foreach (ZoneBoss b in d.Bosses) Unresolved(f, b.Id, "encounter", b.Encounter);
        foreach (ZoneEvent e in d.Events) Unresolved(f, e.Id, "event", e.Definition);
    }

    private static void Unresolved(List<ZoneFinding> f, string id, string what, string reference) =>
        f.Add(Warning("UnresolvedReference", id, $"{what} '{reference}' is not checked until its system exists."));

    private static bool Inside(ZoneFloor f, ZoneCell c) => c.X >= 0 && c.Y >= 0 && c.X < f.Width && c.Y < f.Length;
    private static int Index(ZoneFloor f, ZoneCell c) => c.Y * f.Width + c.X;
    private static bool Walkable(ZoneFloor f, ZoneCell c) =>
        Inside(f, c) && f.Tiles[Index(f, c)] is { Walkable: true, Hole: false };

    private static ZoneFinding Error(string code, string? id, string message) => new(ZoneSeverity.Error, code, id, message);
    private static ZoneFinding Warning(string code, string? id, string message) => new(ZoneSeverity.Warning, code, id, message);
}
```

- [ ] **Step 5: Lancer les tests**

Run: `dotnet test Vortex.Database.Tests --filter "FullyQualifiedName~ZoneValidator"` → PASS (13). Run: `node scripts/hooks/check-architecture-walls.mjs` → « 7 checked ». Run: `dotnet test Vortex.Hosting.Tests --filter "FullyQualifiedName~HostModuleRegistration"` → PASS.

- [ ] **Step 6: Commit** — tous les fichiers de la tâche, par chemins (`git commit -o …`), message « zones: projet Vortex.World et validateur ».

---

### Task 3: Tables `zones` et `zone_versions`

**Files:**
- Create: `Vortex.Database/Entities/World/ZoneEntity.cs`, `Vortex.Database/Entities/World/ZoneVersionEntity.cs`
- Modify: `Vortex.Database/Context/VortexDbContext.cs`, `Vortex.Database/Migrations/*InitialCreate*` (régénérés), `DATA-MODEL.md` (deux lignes)

**Interfaces:**
- Produces: `DbSet<ZoneEntity> Zones`, `DbSet<ZoneVersionEntity> ZoneVersions` ; `ZoneVersionStatus { Draft = 0, Published = 1 }` (persisté : ne jamais renuméroter).

- [ ] **Step 1: Entités** (skill `changing-the-schema`, étape 1)

```csharp
// Vortex.Database/Entities/World/ZoneEntity.cs
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Vortex.Database.Entities.World;

[Table("zones")]
[Index(nameof(Key), IsUnique = true)]
[Index(nameof(RoomEntityId), IsUnique = true)]
public class ZoneEntity : VortexEntity
{
    /// <summary>The document's settings.id (`village_ambre`).</summary>
    [Column("zone_key")]
    [MaxLength(64)]
    public required string Key { get; set; }

    [Column("room_id")]
    public required int RoomEntityId { get; set; }

    [Column("published_version")]
    public int? PublishedVersion { get; set; }

    [Column("locked_by_account_id")]
    public int? LockedByAccountId { get; set; }

    [Column("locked_at")]
    public DateTime? LockedAt { get; set; }
}
```

```csharp
// Vortex.Database/Entities/World/ZoneVersionEntity.cs
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Vortex.Database.Entities.World;

/// <summary>Persisted: never renumber.</summary>
public enum ZoneVersionStatus { Draft = 0, Published = 1 }

[Table("zone_versions")]
[Index(nameof(ZoneEntityId), nameof(Version), IsUnique = true)]
public class ZoneVersionEntity : VortexEntity
{
    [Column("zone_id")]
    public required int ZoneEntityId { get; set; }

    [Column("version")]
    public required int Version { get; set; }

    [Column("schema_version")]
    public required int SchemaVersion { get; set; }

    [Column("document", TypeName = "longtext")]
    public required string Document { get; set; }

    [Column("author_account_id")]
    public required int AuthorAccountId { get; set; }

    [Column("status")]
    public required ZoneVersionStatus Status { get; set; }

    [Column("comment")]
    [MaxLength(512)]
    public string Comment { get; set; } = string.Empty;
}
```

Dans `VortexDbContext.cs`, à la suite des autres :

```csharp
    public DbSet<ZoneEntity> Zones { get; init; } = null!;

    public DbSet<ZoneVersionEntity> ZoneVersions { get; init; } = null!;
```

- [ ] **Step 2: Régénérer et prouver sur base fraîche**

Run: `node .claude/skills/changing-the-schema/scripts/regenerate-initialcreate.mjs` — lire `Up()` : deux `CreateTable` (`zones`, `zone_versions`), rien d'autre de changé.
Run: `pwsh .claude/skills/changing-the-schema/scripts/fresh-db.ps1 -Name vortex_zone_check` → OK.
Run: `dotnet test Vortex.Database.Tests` → PASS (dont `SeedCatalogTests`).

- [ ] **Step 3: Commit** — entités, contexte, migration régénérée, `DATA-MODEL.md` ; message « zones: tables zones et zone_versions ».

---

### Task 4: Enregistrer, publier, revenir, verrouiller

**Files:**
- Create: `Vortex.Primitives/World/IZoneEditorService.cs`, `Vortex.World/ZoneEditorService.cs`
- Modify: `Vortex.World/WorldModule.cs`
- Test: `Vortex.Database.Tests/World/ZoneEditorServiceTests.cs`

**Interfaces:**
- Consumes: Task 2 `ZoneValidator`, Task 3 entités, `IGrainFactory.GetRoomGrain(RoomId).ReloadZoneAsync()` (Task 6 ; dans cette tâche l'appel passe par un `Func<RoomId, Task>` injectable pour les tests, voir ci-dessous).
- Produces (dans `Vortex.Primitives/World/IZoneEditorService.cs`) :

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Vortex.Primitives.World;

public sealed record ZoneSummary(string Key, string Name, int RoomId, int? PublishedVersion, int LatestVersion, int? LockedByAccountId);

public sealed record ZoneVersionInfo(int Version, int AuthorAccountId, System.DateTime CreatedAt, bool Published, string Comment);

public sealed record ZoneSaveResult(bool Saved, int? Version, bool Published, IReadOnlyList<ZoneFinding> Findings);

public sealed record FurniturePaletteEntry(int Definition, string Class, string Name, bool Wall, int Width, int Length, int States, bool CanWalk, bool CanSit, double StackHeight);

public interface IZoneEditorService
{
    Task<IReadOnlyList<ZoneSummary>> ListAsync(CancellationToken ct);
    Task<ZoneSummary> CreateAsync(string key, string name, int accountId, string? fromTemplate, CancellationToken ct);
    Task<string?> GetDocumentAsync(string key, int? version, CancellationToken ct);
    Task<IReadOnlyList<ZoneVersionInfo>> GetVersionsAsync(string key, CancellationToken ct);
    IReadOnlyList<ZoneFinding> Validate(string json);
    Task<ZoneSaveResult> SaveAsync(string key, string json, int accountId, string comment, CancellationToken ct);
    Task<ZoneSaveResult> PublishAsync(string key, int version, CancellationToken ct);
    Task<bool> TryLockAsync(string key, int accountId, bool force, CancellationToken ct);
    Task UnlockAsync(string key, int accountId, CancellationToken ct);
    IReadOnlyList<FurniturePaletteEntry> GetPalette();
}
```

- [ ] **Step 1: Écrire les tests (SQLite)**

`Vortex.Database.Tests/World/ZoneEditorServiceTests.cs` — ouvre la base avec `SqliteTestDb.OpenAsync()`, insère `players` (`__world__`, id 1) et `room_models` (`model_a`, id 1) par SQL brut comme `SeedFurnitureAsync`, construit le service avec un `IFurnitureDefinitionProvider` qui connaît `ZoneSamples.Throne`, `PublishOnSave = false`, `LockMinutes = 30`, et un `reload` qui enregistre les rooms rechargées :

```csharp
[Fact]
public async Task SavingThenPublishing_MovesThePointer_AndReloadsTheRoom()
{
    ZoneSummary zone = await _service.CreateAsync("village", "Village", Author, null, default);
    (await _service.TryLockAsync("village", Author, false, default)).Should().BeTrue();

    ZoneSaveResult saved = await _service.SaveAsync("village", Json(), Author, "first", default);
    saved.Saved.Should().BeTrue();
    saved.Published.Should().BeFalse();

    ZoneSaveResult published = await _service.PublishAsync("village", saved.Version!.Value, default);

    published.Published.Should().BeTrue();
    (await _service.ListAsync(default)).Single().PublishedVersion.Should().Be(1);
    _reloaded.Should().Equal(zone.RoomId);
}

[Fact]
public async Task AnInvalidDocument_IsNotSaved()
{
    await CreateLockedAsync();

    ZoneSaveResult result = await _service.SaveAsync("village", "{\"schemaVersion\":1}", Author, "", default);

    result.Saved.Should().BeFalse();
    result.Findings.Should().Contain(f => f.Code == "Json");
    (await _service.GetVersionsAsync("village", default)).Should().BeEmpty();
}

[Fact]
public async Task AnotherAccount_CannotSaveWhileTheLockIsHeld()
{
    await CreateLockedAsync();

    (await _service.TryLockAsync("village", Other, false, default)).Should().BeFalse();
    (await _service.SaveAsync("village", Json(), Other, "", default)).Findings
        .Should().Contain(f => f.Code == "Locked");
    (await _service.TryLockAsync("village", Other, true, default)).Should().BeTrue();
}

[Fact]
public async Task RollingBack_PublishesAnEarlierVersion()
{
    await CreateLockedAsync();
    await _service.SaveAsync("village", Json(), Author, "v1", default);
    await _service.SaveAsync("village", Json("Village 2"), Author, "v2", default);
    await _service.PublishAsync("village", 2, default);

    await _service.PublishAsync("village", 1, default);

    (await _service.GetDocumentAsync("village", null, default)).Should().Contain("\"name\":\"Village\"");
}

[Fact]
public async Task PublishOnSave_PublishesEverySave()
{
    UseConfig(new WorldConfig { PublishOnSave = true });
    await CreateLockedAsync();

    (await _service.SaveAsync("village", Json(), Author, "", default)).Published.Should().BeTrue();
}
```

avec `Json(string name = "Village") => ZoneJson.Serialize(ZoneSamples.Minimal() with { Settings = new ZoneSettings { Id = "village", Name = name } })` et `GetDocumentAsync(key, null)` = version publiée.

- [ ] **Step 2: Lancer pour voir l'échec** → compilation (`ZoneEditorService` absent).

- [ ] **Step 3: Écrire le service**

`Vortex.World/ZoneEditorService.cs` — points imposés :

```csharp
public sealed class ZoneEditorService(
    IDbContextFactory<VortexDbContext> db,
    ZoneValidator validator,
    IFurnitureDefinitionProvider definitions,
    IOptions<WorldConfig> config,
    ZoneRoomReloader reload,
    ILogger<ZoneEditorService> logger
) : IZoneEditorService
```

- `ZoneRoomReloader` est un `public sealed class ZoneRoomReloader(Func<RoomId, Task> reload)` ; `WorldModule` l'enregistre avec `sp => new ZoneRoomReloader(id => sp.GetRequiredService<IGrainFactory>().GetRoomGrain(id).ReloadZoneAsync())` (Task 6 ajoute la méthode) ; les tests passent leur propre `Func`.
- `CreateAsync` : clé `^[a-z0-9_]{3,64}$` sinon `ArgumentException` ; crée la room par SQL brut (colonnes et défauts de `scripts/sql/dev_zone.sql` : `name`, `description`, `player_id` = `__world__`, `model_id` = `model_a`), puis la ligne `zones` ; si `fromTemplate`, copie le document publié du gabarit en version 1 (statut `Draft`) en remplaçant `settings.id`/`name`.
- `Validate(json)` : `ZoneJson.Deserialize` dans un `try` ; `JsonException` → `[new ZoneFinding(Error, "Json", null, ex.Message)]` ; sinon `validator.Validate(doc, clés de toutes les zones)` ; et `settings.id` différent de la clé → erreur `KeyMismatch`.
- `SaveAsync` : verrou tenu par `accountId` et non expiré (`LockedAt + LockMinutes > UtcNow`) sinon constat `Locked` ; validation ; une erreur → `Saved = false` ; sinon nouvelle ligne `zone_versions` (`Version = max + 1`, `SchemaVersion = 1`, `Status = Draft`), renouvelle `LockedAt` ; si `PublishOnSave`, appelle `PublishAsync`.
- `PublishAsync` : revalide la version (les autres zones ont pu changer) ; erreurs → `Saved = true, Published = false` avec les constats ; sinon, **dans une transaction** (`await using var tx = await ctx.Database.BeginTransactionAsync(ct)`), met `zones.published_version` et `status = Published`, commit, puis `await reload.ReloadAsync(new RoomId(zone.RoomEntityId))` (journalisé, jamais propagé : la publication est faite).
- `TryLockAsync` : prend si libre, expiré ou déjà à soi ; `force = true` prend toujours et journalise `"Zone {Key} lock forced by {Account}"`.
- `GetPalette` : `definitions` ne liste pas tout — ajouter à `IFurnitureDefinitionProvider` une méthode `IReadOnlyCollection<FurnitureDefinitionSnapshot> All()` (implémentation : `_definitions.ById.Values`) et mapper vers `FurniturePaletteEntry` (Floor/Wall seulement).

Enregistrer dans `WorldModule` : `ZoneRoomReloader`, `ZoneEditorService` en `IZoneEditorService` (singleton).

- [ ] **Step 4: Tests** → PASS (5). `dotnet test Vortex.Database.Tests` → PASS.

- [ ] **Step 5: Commit** — « zones: enregistrement, publication, retour arrière et verrou ».

---

### Task 5: Une room-zone tire carte et décor du document

**Files:**
- Create: `Vortex.Primitives/World/IZoneRoomSource.cs`, `Vortex.World/ZoneRoomSource.cs`, `Vortex.Rooms/Grains/Zones/ZoneRoomLoader.cs`
- Modify: `Vortex.Rooms/Grains/RoomGrain.cs` (constructeur, `HydrateRoomStateAsync`), `Vortex.Rooms/Grains/RoomLiveState.cs` (`ZoneDocument? Zone`), `Vortex.Rooms/Grains/Modules/RoomFurniModule.cs` (`EnsureFurniLoadedAsync`), `Vortex.Rooms/Grains/RoomGrain.Furni.cs` (`FlushDirtyItemsAsync`), `Vortex.Rooms.Tests/Support/RoomHarness.cs`, `Vortex.Rooms.Tests/Grains/TickBoundaryTests.cs`, `Vortex.World/WorldModule.cs`
- Test: `Vortex.Rooms.Tests/Zones/ZoneRoomTests.cs`

**Interfaces:**
- Consumes: Task 4 tables, Task 1 records.
- Produces:

```csharp
namespace Vortex.Primitives.World;

public interface IZoneRoomSource
{
    /// <summary>The published document of the zone this room runs, or null for an ordinary room.</summary>
    Task<ZoneDocument?> GetPublishedAsync(RoomId roomId, CancellationToken ct);
}
```

`ZoneRoomLoader` (internal static, Vortex.Rooms) :
- `RoomModelSnapshot CompileModel(ZoneDocument doc, int roomId, IRoomModelProvider models)`
- `(IReadOnlyList<IRoomFloorItem> Floor, IReadOnlyList<IRoomWallItem> Wall) BuildDecor(ZoneDocument doc, PlayerId owner, IFurnitureDefinitionProvider defs, ILogger log)`

- [ ] **Step 1: Écrire les tests**

`Vortex.Rooms.Tests/Zones/ZoneRoomTests.cs` (le harnais reçoit un `ZoneDocument? Zone` qui alimente un faux `IZoneRoomSource`, et `TestDefinitions` pour les définitions) :

```csharp
[Fact]
public async Task AZone_TakesItsMapFromTheFloor()
{
    RoomHarness harness = await RoomHarness.CreateZoneAsync(ZoneSamples.Minimal() with
    {
        Floor = ZoneSamples.Minimal().Floor with { Tiles = Heights(6, 6, at: (4, 4), height: 3) },
    });

    harness.Grain._state.Model!.Width.Should().Be(6);
    harness.Grain._state.Model.BaseHeights[4 * 6 + 4].Value.Should().Be(3);
    harness.Grain._state.Model.DoorX.Should().Be(0);
    harness.Grain._state.Model.DoorY.Should().Be(2);
}

[Fact]
public async Task ANonWalkableTile_IsClosed()
{
    ZoneDocument doc = ZoneSamples.Minimal();
    doc.Floor.Tiles[1 * 6 + 1] = new ZoneTile { Walkable = false };

    RoomHarness harness = await RoomHarness.CreateZoneAsync(doc);

    harness.Grain._state.Model!.BaseFlags[1 * 6 + 1].Should().NotHaveFlag(RoomTileFlags.Walkable);
}

[Fact]
public async Task AZone_LoadsItsDecor_WithoutReadingFurnitureRows()
{
    RoomHarness harness = await RoomHarness.CreateZoneAsync(ZoneSamples.EveryLayer());

    await harness.Grain.FurniModule.EnsureFurniLoadedAsync(default);

    harness.Grain._state.ItemsById.Values.Should().ContainSingle()
        .Which.Definition.Id.Should().Be(ZoneSamples.Throne);
    harness.ItemsProviderCalls.Should().Be(0);
}

[Fact]
public async Task ADecorObjectWithAnUnknownDefinition_IsLeftOut()
{
    RoomHarness harness = await RoomHarness.CreateZoneAsync(ZoneSamples.Minimal() with
    {
        Decor = [new ZoneDecorObject { Id = "gone", Definition = 424242, Class = "gone", X = 1, Y = 1 }],
    });

    await harness.Grain.FurniModule.EnsureFurniLoadedAsync(default);

    harness.Grain._state.IsFurniLoaded.Should().BeTrue();
    harness.Grain._state.ItemsById.Should().BeEmpty();
}

[Fact]
public async Task AZone_NeverFlushesItsDecor()
{
    RoomHarness harness = await RoomHarness.CreateZoneAsync(ZoneSamples.EveryLayer());
    await harness.Grain.FurniModule.EnsureFurniLoadedAsync(default);
    harness.Grain._state.DirtyItemIds.Add(harness.Grain._state.ItemsById.Keys.Single());

    await harness.FlushItemsAsync();

    harness.PersistenceBatches.Should().BeEmpty();
    harness.Grain._state.DirtyItemIds.Should().BeEmpty();
}
```

À ajouter au harnais : `CreateZoneAsync(ZoneDocument)` (construit le grain avec un `IZoneRoomSource` qui rend ce document, appelle `HydrateRoomStateAsync` par réflexion interne via une méthode `internal Task HydrateForTestsAsync()` exposée sur `RoomGrain` — `Vortex.Rooms` a déjà `InternalsVisibleTo` vers `Vortex.Rooms.Tests` — avec une base InMemory contenant la ligne `rooms` du harnais), `ItemsProviderCalls` (compteur du faux `IRoomItemsProvider`), `PersistenceBatches` (le faux `IRoomPersistenceGrain` de `BuildGrainFactory` enregistre `EnqueueDirtyItemsAsync`), `FlushItemsAsync()` (appelle `FlushDirtyItemsAsync` rendu `internal`). Dans `CreateAsync` classique, le faux `IZoneRoomSource` rend `null`. `TickBoundaryTests.cs:111` reçoit `FakeProxy.Create<IZoneRoomSource>(_ => Task.FromResult<ZoneDocument?>(null))` à la même position que dans le constructeur.

- [ ] **Step 2: Lancer pour voir l'échec** → compilation.

- [ ] **Step 3: Implémenter**

1. `RoomGrain` reçoit `IZoneRoomSource zoneSource` en **dernier** paramètre du constructeur (garder le commentaire existant sur `params object[]`) ; champ `internal readonly IZoneRoomSource _zoneSource`.
2. `HydrateRoomStateAsync`, juste après `_state.Model = _roomModelProvider.GetModelById(...)` :

```csharp
            ZoneDocument? zone = await _zoneSource.GetPublishedAsync(_state.RoomId, ct);

            if (zone is not null)
            {
                _state.Zone = zone;
                _state.Model = ZoneRoomLoader.CompileModel(zone, entity.Id, _roomModelProvider);
            }
```

3. `ZoneRoomLoader.CompileModel` : construit la chaîne de modèle Habbo ligne par ligne (`x` pour un trou, sinon `"0123456789abcdefghijklmnopqrstuvw"[height]`), lignes jointes par `\r` ; appelle `models.CompileCustomModel(-roomId, doc.Settings.Name, model, door.X, door.Y, (Rotation)doc.Floor.DoorDirection)` ; puis pour chaque case `Walkable == false` retire `RoomTileFlags.Walkable` de `BaseFlags[index]` (le tableau est celui du snapshot rendu ; le copier avant de le modifier si le type est immuable, et rendre un `with { BaseFlags = copie }`).
4. `RoomFurniModule.EnsureFurniLoadedAsync` : si `_roomGrain._state.Zone is { } zone`, remplacer l'appel à `_itemsLoader.LoadByRoomIdAsync` par `ZoneRoomLoader.BuildDecor(zone, (PlayerId)_roomGrain._state.RoomSnapshot.OwnerId, _roomGrain._definitionProvider, _roomGrain._logger)` et un dictionnaire de propriétaires `{ owner → RoomSnapshot.OwnerName }` ; la suite (attache tolérante, `AttachLoadedItemAsync`) est inchangée.
5. `BuildDecor` : pour chaque `ZoneDecorObject` d'index `i` → `TryGetDefinition` ; absent ou pas `Floor` → `log.LogWarning("Zone decor {Id} uses unknown furniture {Definition}; left out.", …)` et suivant ; sinon `new RoomFloorItem { ObjectId = i + 1, OwnerId = owner, OwnerName = string.Empty, Definition = def }`, `SetPosition(o.X, o.Y)`, `SetPositionZ(o.Z)`, `SetRotation((Rotation)o.Rotation)`, `SetExtraData(o.State.ToString(CultureInfo.InvariantCulture))`. Objets muraux : ids `Decor.Count + j + 1`, `RoomWallItem`, position `SetPosition(o.X, o.Y)` et `SetWallOffset(o.OffsetX)`, rotation `Side == Left ? 2 : 4`.
6. `RoomGrain.Furni.cs` `FlushDirtyItemsAsync` : en tête,

```csharp
        // A zone's decor has no furniture rows: its ids can equal another room's rows, and a flush
        // would overwrite them. It only changes by publishing a new version.
        if (_state.Zone is not null)
        {
            _state.DirtyItemIds.Clear();

            return;
        }
```

7. `Vortex.World/ZoneRoomSource.cs` : `GetPublishedAsync` lit `zones` par `room_id`, puis la version publiée ; `null` si pas de zone ou pas de version publiée ; désérialise avec `ZoneJson.Deserialize`, et une `JsonException` est journalisée et rend `null` (la room s'ouvre comme une room classique plutôt que pas du tout). Enregistrer `IZoneRoomSource` dans `WorldModule`.

- [ ] **Step 4: Tests** → `dotnet test Vortex.Rooms.Tests` PASS (tous, dont les 5 nouveaux).

- [ ] **Step 5: Commit** — « zones: une room-zone tire carte et décor du document publié ».

---

### Task 6: Rechargement à chaud

**Files:**
- Modify: `Vortex.Primitives/Rooms/Grains/IRoomCore.cs` (`Task ReloadZoneAsync();`), `Vortex.Rooms/Grains/RoomGrain.cs`, `Vortex.World/WorldModule.cs` (le `Func` de `ZoneRoomReloader` appelle désormais `ReloadZoneAsync`)
- Test: `Vortex.Rooms.Tests/Zones/ZoneReloadTests.cs`

**Interfaces:**
- Produces: `IRoomCore.ReloadZoneAsync()`.

Comportement (spec §6.7, version 1) : la room transfère chaque occupant vers elle-même puis se désactive ; la prochaine activation charge la version publiée et chacun y rentre **par la porte**. Garder la case de chacun est remis à plus tard (spec amendée en même temps que ce plan).

- [ ] **Step 1: Test**

```csharp
[Fact]
public async Task ReloadingAnOccupiedZone_ForwardsEveryOccupant()
{
    RoomHarness harness = await RoomHarness.CreateZoneAsync(ZoneSamples.Minimal());
    harness.PutRealPlayerOnTile(new PlayerId(7), 1, 1);
    harness.PutRealPlayerOnTile(new PlayerId(8), 2, 2);

    await harness.Grain.ReloadZoneAsync();

    harness.SentToPlayers.Should().BeEquivalentTo(new[]
    {
        (new PlayerId(7), typeof(RoomForwardMessageComposer)),
        (new PlayerId(8), typeof(RoomForwardMessageComposer)),
    });
    harness.Deactivations.Should().Be(1);
}

[Fact]
public async Task ReloadingAnEmptyZone_OnlyDeactivates()
{
    RoomHarness harness = await RoomHarness.CreateZoneAsync(ZoneSamples.Minimal());

    await harness.Grain.ReloadZoneAsync();

    harness.SentToPlayers.Should().BeEmpty();
    harness.Deactivations.Should().Be(1);
}
```

Le harnais : `SentToPlayers` (le faux `IPlayerPresenceGrain` de `BuildGrainFactory` enregistre `(clé, type du composer)` sur `SendComposerAsync`) ; `Deactivations` (compter `DeactivateOnIdle` comme `PresenceSessionLifetimeTests.Recorder` le fait, via le callback d'observation de `GrainActivationContext.CreateWithIntegerKey`).

- [ ] **Step 2: Échec** → compilation.

- [ ] **Step 3: Implémenter** dans `RoomGrain.cs` :

```csharp
    public async Task ReloadZoneAsync()
    {
        RoomForwardMessageComposer forward = new() { RoomId = _state.RoomId };

        foreach (PlayerId player in _state.AvatarsByPlayerId.Keys.ToArray())
        {
            await _grainFactory.GetPlayerPresenceGrain(player).SendComposerAsync(forward);
        }

        DeactivateOnIdle();
    }
```

et `WorldModule` : `new ZoneRoomReloader(id => sp.GetRequiredService<IGrainFactory>().GetRoomGrain(id).ReloadZoneAsync())`.

- [ ] **Step 4: Tests** → PASS ; `node scripts/hooks/check-authorization-surface.mjs` (méthode de grain ajoutée : relire, puis `--update` si la sortie la liste comme seul changement).

- [ ] **Step 5: Commit** — « zones: rechargement à chaud d'une zone publiée ».

---

### Task 7: Routes `/api/editor` et droits

**Files:**
- Create: `Vortex.WebApi/Hosting/WebApiEndpoints.Editor.cs`
- Modify: `Vortex.WebApi/Hosting/WebApiEndpoints.cs` (appel `MapEditor(app)` ; rendre la classe `partial`), `Vortex.WebApi/WebApiPlugin.cs` (`UseHostService<IZoneEditorService>()`, `UseHostService<IPermissionService>()`), `Vortex.WebApi/Hosting/WebApiWebHost.cs` (`ForwardSingletons` : les deux), `Vortex.WebApi.Tests/WebApiTestFactory.cs` (faux des deux), `Vortex.Primitives/Permissions/Capabilities.cs` (`World.ZoneEdit = "world.zone.edit"`, `World.ZonePublish = "world.zone.publish"`, ajoutés à `Capabilities.All`), `Vortex.Permissions/DefaultRoles.cs` (rôle `owner`), `Vortex.WebApi/openapi.json` (régénéré : `UPDATE_OPENAPI=1`)
- Test: `Vortex.WebApi.Tests/EditorEndpointsTests.cs`

**Interfaces:**
- Consumes: `IZoneEditorService` (Task 4), `IPermissionService.ResolveForAccountAsync`, `ctx.AccountId(sessions)`.

Routes (toutes : session requise → 401 ; capability manquante → 403 `ForbiddenError`) :

| Route | Capability | Réponse |
|---|---|---|
| `GET /api/editor/zones` | edit | `ZoneSummary[]` |
| `POST /api/editor/zones` `{key, name, fromTemplate?}` | edit | `ZoneSummary` ; 400 si clé invalide ; 409 si existe |
| `GET /api/editor/zones/{key}/versions` | edit | `ZoneVersionInfo[]` |
| `GET /api/editor/zones/{key}/document?version=` | edit | le JSON brut (`application/json`) ; 404 |
| `POST /api/editor/zones/{key}/validate` (corps = document) | edit | `ZoneFinding[]` |
| `POST /api/editor/zones/{key}/versions?comment=` (corps = document) | edit | `ZoneSaveResult` ; **400** si `Saved = false` |
| `POST /api/editor/zones/{key}/publish/{version}` | publish | `ZoneSaveResult` ; 400 si refusé |
| `POST /api/editor/zones/{key}/lock?force=` | edit (+ publish si `force`) | 200 / 409 |
| `DELETE /api/editor/zones/{key}/lock` | edit | 200 |
| `GET /api/editor/furniture` | edit | `FurniturePaletteEntry[]` |

Le corps document est lu brut (`await new StreamReader(ctx.Request.Body).ReadToEndAsync(ct)`, limite 4 Mo) pour que le service fasse lui-même la désérialisation et rende des constats.

- [ ] **Step 1: Tests**

```csharp
[Fact]
public async Task WithoutTheCapability_TheEditorIsForbidden()
{
    await using WebApiTestFactory f = new(capabilities: []);

    (await f.AuthenticatedClient.GetAsync("/api/editor/zones")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
}

[Fact]
public async Task SavingGarbage_IsABadRequestWithFindings()
{
    await using WebApiTestFactory f = new(capabilities: [Capabilities.World.ZoneEdit]);

    HttpResponseMessage r = await f.AuthenticatedClient.PostAsync(
        "/api/editor/zones/village/versions", new StringContent("{not json", Encoding.UTF8, "application/json"));

    r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await r.Content.ReadAsStringAsync()).Should().Contain("\"code\":\"Json\"");
}

[Fact]
public async Task PublishingNeedsThePublishCapability()
{
    await using WebApiTestFactory f = new(capabilities: [Capabilities.World.ZoneEdit]);

    (await f.AuthenticatedClient.PostAsync("/api/editor/zones/village/publish/1", null))
        .StatusCode.Should().Be(HttpStatusCode.Forbidden);
}
```

`WebApiTestFactory` : paramètre `string[] capabilities` (défaut : les deux) → faux `IPermissionService` qui rend `new PermissionSet(capabilities, [])` (lire le constructeur de `PermissionSet`) ; faux `IZoneEditorService` dont `SaveAsync` rend `new ZoneSaveResult(false, null, false, [new(Error, "Json", null, "bad")])` quand le corps n'est pas du JSON, et dont le reste rend des valeurs vides ; `AuthenticatedClient` = client avec le cookie de session (comme `WebApiEndpointsTests` le fait déjà).

- [ ] **Step 2: Échec** → 404 sur les routes.

- [ ] **Step 3: Implémenter** `WebApiEndpoints.Editor.cs` sur le modèle des routes existantes (`Results<…>`, `TypedResults`, `WithName`, `WithTags("Editor")`), avec un garde partagé :

```csharp
    private static async Task<IResult?> GuardAsync(
        HttpContext ctx, WebApiSessionStore sessions, IPermissionService permissions, string capability, CancellationToken ct)
    {
        int? accountId = ctx.AccountId(sessions);

        if (accountId is null)
        {
            return Unauthorized();
        }

        PermissionSet set = await permissions.ResolveForAccountAsync(accountId.Value, ct).ConfigureAwait(false);

        return set.Has(capability) ? null : new ForbiddenError("pocket.editor.forbidden");
    }
```

Puis `UseHostService<…>` dans `BorrowFromHost`, les deux lignes de `ForwardSingletons`, les capabilities et le rôle `owner`.

- [ ] **Step 4: Vérifier**

Run: `dotnet test Vortex.WebApi.Tests` → PASS ; `UPDATE_OPENAPI=1 dotnet test Vortex.WebApi.Tests --filter "FullyQualifiedName~OpenApiDocument"` puis sans la variable → PASS ; `node scripts/hooks/check-webapi-forwarding.mjs` → OK ; `dotnet test Vortex.Permissions.Tests` → PASS.

- [ ] **Step 5: Commit** — « zones: routes /api/editor et droits de construction ».

---

### Task 8: Preuve en vrai et gate

**Files:**
- Create: `.claude/skills/run-vortex-emulator/zone-publish.mjs` (script de preuve, gardé : il resservira à l'éditeur)
- Modify: `.claude/skills/run-vortex-emulator/SKILL.md` (une ligne), `docs/architecture.md` (Flow 4 — publication d'une zone, cinq lignes)

- [ ] **Step 1: Script** — en Node, sur le modèle de `driver.mjs` (`request`, cookie de session) : se connecte avec le compte du driver (propriétaire : `scripts/sql` ou `/sql-fix` pour lui donner `owner` sur la base de test, jamais sur `turbo`), `POST /api/editor/zones {key:"proof_zone"}`, prend le verrou, enregistre `ZoneSamples.Minimal()` (copié en JSON dans le script avec un trône à (3,3)), publie, affiche `roomId`.
- [ ] **Step 2: Jouer dedans** — `node .claude/skills/run-vortex-emulator/driver.mjs play --room=<roomId>` (si `play` n'accepte pas de room, l'ajouter : un paramètre qui remplace `world.start_room_id` pour cette exécution) → l'entrée liste `Objects` avec le trône ; un pas fait `UserUpdate`. Republier une version où le trône est à (4,4) pendant que le joueur est dedans → le driver reçoit `RoomForward` et rentre.
- [ ] **Step 3: Gate** — `dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudQualityGate` → vert ; `driver.mjs smoke` → OK.
- [ ] **Step 4: Commit** — « zones: preuve en vrai (publication puis entrée) ».

---

## Auto-relecture

- **Couverture de la spec :** §3 → Tasks 3, 5 ; §4 → Task 1 ; §5 → Task 2 ; §6.1–6.5 → Tasks 4, 7 ; §6.6 (ticket de test) **reporté à la spec 3** ; §6.7 → Task 6 (entrée par la porte en v1) ; §8 → tests de chaque tâche + Task 8. Spec amendée en conséquence (§4.1 référence par id + classe, §6.6, §6.7).
- **Types :** `ZoneDocument`, `ZoneFinding`, `IZoneEditorService`, `IZoneRoomSource`, `ZoneRoomReloader`, `ReloadZoneAsync` — mêmes noms dans toutes les tâches.
- **Review Focus :** les 5 lignes ont leur test (Tasks 5, 7, 4, 6, 5).
