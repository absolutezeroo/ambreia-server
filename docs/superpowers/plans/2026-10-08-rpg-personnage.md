# RPG F2 — Personnage : plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** faire de chaque avatar un personnage — une classe, un niveau, de l'XP, des stats calculées, une dernière zone où revenir, et 60 s de grâce à la coupure.

**Architecture:** l'état RPG d'un joueur appartient à `PlayerCharacterGrain` (clé = id du joueur, projet plateforme `Vortex.Rpg.Characters`), hydraté depuis `player_characters` et vidé en différé. Les stats sont une somme de sources (`IStatSource`, déclarées par DI) passée à un `StatCalculator` pur dans `Vortex.Rpg`. Le cœur (SSO, présence, web API) ne voit que le contrat `IPlayerCharacterGrain` de `Vortex.Primitives`.

**Tech Stack:** C# .NET 10, Orleans (silo unique), EF Core 9 + Pomelo MySQL (SQLite en test), xUnit + FluentAssertions.

**Spec:** `docs/superpowers/specs/2026-10-08-rpg-character-design.md` (dépend de `2026-10-08-rpg-content-design.md`, livrée : `IRpgContent`, `IContentReferences`, `ContentReloadedEvent`).

## Global Constraints

- 5 personnages par compte (`WebApiConfig.MaxAvatarsPerAccount = 5`, inchangé).
- Niveau max 20 (contenu `progression.default`).
- Classe choisie à la création, définitive ; une spécialisation par classe en V1, attribuée d'office (la première de `specializations`).
- Délai de grâce à la déconnexion : 60 s.
- À la connexion : entrée de test en attente → room en grâce → dernière zone publiée → `world.start_room_id`.
- Écriture par le grain seul : au plus toutes les 30 s (`CharacterConfig.FlushIntervalMs = 30000`), à la désactivation, et immédiatement à une montée de niveau ; jamais par gain d'XP.
- Le client n'envoie ni stats, ni XP, ni niveau.
- Un type par fichier (C# et TS) ; modifications de code par Edit/Write ; commits en français, chemins explicites, sur `main`.
- Le cœur (`Vortex.PacketHandlers`, `Vortex.Players`) ne référence pas le RPG : contrats dans `Vortex.Primitives`.
- Un id de contenu dans `player_characters` n'est jamais réécrit depuis la mémoire du grain (AGENTS.md, Gotchas) : le vidage n'écrit que `level`, `xp`, `last_room_id`.

## Décisions de ce plan (écarts assumés à la spec, à valider par le propriétaire)

1. **Sources de stats déclarées par DI, pas balayées** (spec §6.1 dit « découverte par balayage ») : `services.AddSingleton<IStatSource, ClassLevelStatSource>()`, comme les sortes de contenu (amendement de la spec Contenu). La spec Objets ajoutera sa ligne pour `EquipmentStatSource`.
2. **`PlayerConfig.DisconnectGraceSeconds`** de la spec devient `PlayerPresenceConfig.DisconnectGraceSeconds` (la classe `PlayerConfig` n'existe pas ; section `Vortex:PlayerPresence`).
3. **Commande de test d'XP = route d'administration** `POST /api/admin/characters/{playerId}/xp` (capability `rpg.character.grant_xp`), pas une commande console : la console ne se pilote que par l'entrée standard, que le driver n'a pas.
4. **Exemple §4 de la spec aligné sur `content/class/warrior.json`** (PV de base 60, car PV max = PV + FOR × 5 = 120 au niveau 1).
5. **Contenu changé = stats recalculées à la demande** : le grain garde la version du contenu de ses stats et les recalcule (et relit `class_id`) quand `IRpgContent.Version` a changé, au lieu d'un handler de `ContentReloadedEvent` qui devrait joindre chaque grain actif.

## Review Focus

1. **Coupure pendant la marche** → l'avatar s'arrête sur sa case et y reste 60 s ; à la reconnexion, un seul avatar, sur cette case (Tâche 7, `AReconnectWithinGrace_KeepsTheOneAvatar`).
2. **Dernière zone dépubliée ou devenue copie de donjon** → la connexion tombe sur la zone de départ, jamais sur une room morte (Tâche 6, `ALastZoneNoLongerPublished_FallsBackToTheStart`).
3. **Classe retirée par une règle `replace` pendant que le joueur est en ligne** → le grain relit la nouvelle classe ; le vidage suivant ne remet pas l'ancienne (Tâche 5, `AReplacedClass_IsReReadAndNeverWrittenBack`).
4. **Gros gain d'XP d'un coup** (plusieurs niveaux, au-delà du max) → niveaux enchaînés, un `LevelUp` par niveau, arrêt net au niveau 20 avec XP 0, une seule écriture (Tâche 4, `AGrantOverSeveralLevels_StopsAtTheMax`).
5. **Avatar sans classe** (créé avant ce plan) → se connecte, entre en zone, aucun système RPG ne lève ; `GET /api/user/avatars` le montre avec `classId` null (Tâches 3, 8).

---

### Task 1: Le calcul des stats

**Files:**
- Create: `Vortex.Primitives/Rpg/Characters/CharacterStatsSnapshot.cs`, `CharacterStatContribution.cs`
- Create: `Vortex.Rpg/Stats/IStatSource.cs`, `StatSourceContext.cs`, `StatCalculator.cs`
- Modify: `docs/superpowers/specs/2026-10-08-rpg-character-design.md` (§4 : `base.health` 60, `perLevel` comme `content/class/warrior.json`)
- Test: `Vortex.Rpg.Tests/Stats/StatCalculatorTests.cs`

**Interfaces:**
- Produces:
  - `[GenerateSerializer, Immutable] sealed record CharacterStatContribution([property: Id(0)] string Source, [property: Id(1)] int Strength, [property: Id(2)] int Intelligence, [property: Id(3)] int Agility, [property: Id(4)] int Health, [property: Id(5)] int Mana, [property: Id(6)] int Armor, [property: Id(7)] int Speed)`.
  - `[GenerateSerializer, Immutable] sealed record CharacterStatsSnapshot` : `Strength`, `Intelligence`, `Agility`, `MaxHealth`, `MaxMana`, `Armor` (int), `CritPercent`, `DodgePercent` (double), `SpeedPercent`, `Power` (int), `ImmutableArray<CharacterStatContribution> Sources` — `[Id(0..10)]` dans cet ordre.
  - `interface IStatSource { string Name { get; } ValueTask<StatBlock> ContributeAsync(StatSourceContext context, CancellationToken ct); }`
  - `sealed record StatSourceContext(PlayerId PlayerId, ClassDefinition? Class, int Level)`.
  - `static class StatCalculator { static CharacterStatsSnapshot Compute(IReadOnlyList<(string Source, StatBlock Stats)> contributions, ProgressionDefinition? progression, PrimaryStat? primary) }`.

- [ ] **Step 1: Écrire les tests.** Classes construites comme `content/class/*.json` (mêmes valeurs), progression comme `content/progression/default.json` :

```csharp
public sealed class StatCalculatorTests
{
    private static readonly ProgressionDefinition Progression = new()
    {
        Id = "progression.default", Kind = "progression", MaxLevel = 20,
        XpToNext = [100, 150, 220, 300, 400, 520, 660, 820, 1000, 1200, 1450, 1750, 2100, 2500, 2950, 3450, 4000, 4600, 5300],
        HealthPerStrength = 5, ManaPerIntelligence = 5, CritBasePercent = 5, CritPerAgilityPercent = 0.2,
        CritCapPercent = 50, DodgePerAgilityPercent = 0.1, DodgeCapPercent = 30,
    };

    private static StatBlock ClassAt(StatBlock b, StatBlock g, int level) => new()
    {
        Strength = b.Strength + g.Strength * (level - 1), Intelligence = b.Intelligence + g.Intelligence * (level - 1),
        Agility = b.Agility + g.Agility * (level - 1), Health = b.Health + g.Health * (level - 1),
        Mana = b.Mana + g.Mana * (level - 1), Armor = b.Armor + g.Armor * (level - 1), Speed = b.Speed + g.Speed * (level - 1),
    };

    private static readonly StatBlock WarriorBase = new() { Strength = 12, Intelligence = 4, Agility = 6, Health = 60, Mana = 40, Armor = 4 };
    private static readonly StatBlock WarriorGrowth = new() { Strength = 3, Agility = 1, Health = 18, Mana = 2, Armor = 1 };

    [Fact]
    public void AWarriorAtLevelOne_HasTheSpecHealth()
    {
        CharacterStatsSnapshot s = StatCalculator.Compute([("class", ClassAt(WarriorBase, WarriorGrowth, 1))], Progression, PrimaryStat.Strength);

        (s.MaxHealth, s.MaxMana, s.Armor, s.Power).Should().Be((120, 60, 4, 12));
        s.CritPercent.Should().BeApproximately(6.2, 1e-9);
        s.DodgePercent.Should().BeApproximately(0.6, 1e-9);
        s.SpeedPercent.Should().Be(100);
    }

    [Fact]
    public void AWarriorAtLevelTwenty_Grows() =>
        StatCalculator.Compute([("class", ClassAt(WarriorBase, WarriorGrowth, 20))], Progression, PrimaryStat.Strength)
            .MaxHealth.Should().Be(402 + 69 * 5);

    [Fact]
    public void CritAndDodge_StopAtTheirCaps() { /* agilité 1000 → Crit 50, Dodge 30 */ }

    [Fact]
    public void Speed_IsBoundedBetweenFiftyAndTwoHundred() { /* speed −500 → 50, +500 → 200 */ }

    [Fact]
    public void NoStat_IsNegative() { /* une source à −100 partout → tout à 0, vitesse 50 */ }

    [Fact]
    public void EverySource_IsKeptInTheDetail() { /* deux sources → Sources a 2 lignes, noms et valeurs */ }

    [Fact]
    public void WithoutProgression_DerivedCoefficientsAreZero() { /* progression null → MaxHealth = PV de la pile */ }
}
```

Les corps commentés ci-dessus s'écrivent en entier dans le fichier (une assertion chacun, valeurs exactes) : agilité 1000 → `CritPercent == 50`, `DodgePercent == 30` ; `Speed = -500` → `SpeedPercent == 50`, `Speed = 500` → `200` ; une source `Strength = Intelligence = Agility = Health = Mana = Armor = -100` → `Strength, Intelligence, Agility, MaxHealth, MaxMana, Armor, Power` tous 0 et `SpeedPercent == 50` ; deux sources `("class", Str 10)` et `("equipment", Str 2)` → `Strength == 12` et `Sources.Select(x => x.Source)` égal à `["class", "equipment"]` ; progression `null` et `Health 50, Strength 10` → `MaxHealth == 50`, `CritPercent == 0`.

- [ ] **Step 2: Vérifier l'échec** — `dotnet test Vortex.Rpg.Tests --filter StatCalculatorTests` · Expected: échec de compilation (`StatCalculator` introuvable).
- [ ] **Step 3: Implémenter.** `StatCalculator.Compute` : somme champ par champ des contributions ; chaque principale bornée à ≥ 0 ; `MaxHealth = max(0, health + str × hps)`, `MaxMana = max(0, mana + int × mpi)`, `Armor = max(0, armor)`, `Crit = clamp(critBase + agi × cpa, 0, cap)`, `Dodge = clamp(agi × dpa, 0, cap)`, `SpeedPercent = clamp(100 + speed, 50, 200)`, `Power` = la principale finale de `primary` (0 si null) ; coefficients à 0 quand `progression` est null ; `Sources` = une `CharacterStatContribution` par entrée, dans l'ordre reçu. Corriger l'exemple §4 de la spec.
- [ ] **Step 4: Vérifier le succès** · Expected: PASS (7).
- [ ] **Step 5: Commit** — `rpg : calcul des stats d'un personnage`.

---

### Task 2: La table `player_characters`

**Files:**
- Create: `Vortex.Database/Entities/Rpg/PlayerCharacterEntity.cs`
- Modify: `Vortex.Database/Context/VortexDbContext.cs` (`DbSet<PlayerCharacterEntity> PlayerCharacters`)
- Regenerate: `Vortex.Database/Migrations/*InitialCreate*`
- Create: `scripts/sql/2026-10-08-player-characters.sql`

**Interfaces:**
- Produces: `[Table("player_characters")] [Index(nameof(PlayerEntityId), IsUnique = true)] PlayerCharacterEntity : VortexEntity` — `[Column("player_id")] required int PlayerEntityId`, `[ForeignKey] PlayerEntity? PlayerEntity`, `[Column("class_id")][MaxLength(ContentIdLength)] required string ClassId`, `[Column("specialization_id")][MaxLength(ContentIdLength)] required string SpecializationId`, `[Column("level")] int Level = 1`, `[Column("xp")] int Xp`, `[Column("last_room_id")] int? LastRoomId`.

- [ ] **Step 1:** charger la compétence `changing-the-schema` ; écrire l'entité (forme de `SecurityTicketEntity` pour la clé unique vers `players`) et le `DbSet`.
- [ ] **Step 2:** `node .claude/skills/changing-the-schema/scripts/regenerate-initialcreate.mjs` · Expected: `player_characters` dans `Up()`, rien d'autre de nouveau (comparer l'ancien et le nouveau fichier : seules des lignes ajoutées).
- [ ] **Step 3:** `pwsh .claude/skills/changing-the-schema/scripts/fresh-db.ps1 -Name vortex_schema_check` · Expected: migration et import réussis.
- [ ] **Step 4:** `scripts/sql/2026-10-08-player-characters.sql` : le `CREATE TABLE IF NOT EXISTS` tiré de `SHOW CREATE TABLE player_characters` sur la base neuve, puis `UPDATE __EFMigrationsHistory SET MigrationId = '<nouvel id>' WHERE MigrationId = '20261007225517_InitialCreate';`. L'appliquer deux fois à `ambreia-database` (mot de passe par `MYSQL_PWD`) · Expected: aucune erreur, historique sur le nouvel id.
- [ ] **Step 5:** `dotnet test Vortex.Database.Tests` · Expected: PASS. Commit — `base : table player_characters`.

---

### Task 3: Le grain du personnage

**Files:**
- Create: projet `Vortex.Rpg.Characters/Vortex.Rpg.Characters.csproj` (refs `Vortex.Rpg`, `Vortex.Database`, `Vortex.Events`, `Vortex.Logging`, `Vortex.Primitives` ; packages `Microsoft.Orleans.Sdk`, `Microsoft.Extensions.Hosting` ; `InternalsVisibleTo` `Vortex.Database.Tests`), ajouté à `Vortex.Cloud.sln`
- Create: `Vortex.Rpg.Characters/CharactersModule.cs`, `Configuration/CharacterConfig.cs`, `Grains/PlayerCharacterGrain.cs`, `Stats/ClassLevelStatSource.cs`
- Create: `Vortex.Primitives/Rpg/Characters/Grains/IPlayerCharacterGrain.cs`, `CharacterSummary.cs`, `ChooseClassOutcome.cs`
- Create: `Vortex.Primitives/Events/Rpg/CharacterCreatedEvent.cs`, `CharacterStatsChangedEvent.cs`
- Modify: `Vortex.Primitives/Orleans/GrainFactoryExtensions.cs` (`GetPlayerCharacterGrain`), `Vortex.Main/Program.cs` (`AddHostPlugin<CharactersModule>` après `RpgModule`), `Vortex.Main/Vortex.Main.csproj`, `Vortex.Database.Tests/Vortex.Database.Tests.csproj`, `scripts/hooks/check-architecture-walls.mjs` (`'Rpg.Characters'` dans `PLATFORM`)
- Test: `Vortex.Database.Tests/Rpg/PlayerCharacterGrainTests.cs` (SQLite, comme `RpgContentServiceTests`)

**Interfaces:**
- Consumes: `IRpgContent` (`Get<ClassDefinition>`, `Get<ProgressionDefinition>("progression.default")`, `Version`), `StatCalculator`, `IStatSource` (Tâche 1), `PlayerCharacterEntity` (Tâche 2).
- Produces:
  - `interface IPlayerCharacterGrain : IGrainWithIntegerKey` : `Task<CharacterSummary?> GetSummaryAsync()`, `Task<ChooseClassOutcome> ChooseClassAsync(string classId)`, `Task<CharacterStatsSnapshot?> GetStatsAsync()` (les méthodes des Tâches 4 et 6 s'ajoutent ici).
  - `[GenerateSerializer, Immutable] sealed record CharacterSummary([property: Id(0)] string ClassId, [property: Id(1)] string SpecializationId, [property: Id(2)] int Level, [property: Id(3)] int Xp)`.
  - `enum ChooseClassOutcome { Chosen = 0, AlreadyChosen = 1, UnknownClass = 2 }`.
  - `GrainFactoryExtensions.GetPlayerCharacterGrain(this IGrainFactory factory, PlayerId playerId) => factory.GetGrain<IPlayerCharacterGrain>((long)playerId.Value)`.
  - `CharacterConfig { const string SECTION_NAME = "Vortex:Characters"; int FlushIntervalMs = 30000; int MaxXpPerGrant = 100000; }`.
  - `record CharacterCreatedEvent(PlayerId PlayerId, string ClassId, string SpecializationId) : IEvent`, `record CharacterStatsChangedEvent(PlayerId PlayerId) : IEvent`.
  - `CharactersModule : IHostPluginModule` (`Key => "vortex-rpg-characters"`) : `Configure<CharacterConfig>`, `AddSingleton<IStatSource, ClassLevelStatSource>()`.

- [ ] **Step 1: Écrire les tests** (`PlayerCharacterGrainTests`, base SQLite de `SqliteTestDb` ; `IRpgContent` est un `FakeProxy` qui répond depuis un dictionnaire de définitions — le Guerrier et `spec.warrior.guardian` de `content/`, `progression.default` — et une `Version` réglable par le test) :
  - `AnAvatarWithoutAClass_HasNoSummaryAndNoStats` : `GetSummaryAsync()` et `GetStatsAsync()` → null.
  - `ChoosingAClass_CreatesTheCharacterAtLevelOne` : `ChooseClassAsync("class.warrior")` → `Chosen` ; ligne écrite (`level` 1, `xp` 0, `specialization_id` = `spec.warrior.guardian`) ; `CharacterCreatedEvent` publié.
  - `ChoosingTwice_IsRefused` : second appel → `AlreadyChosen`, une seule ligne.
  - `AnUnknownOrRetiredClass_IsRefused` : `class.paladin` → `UnknownClass` ; une classe `Retired = true` → `UnknownClass`.
  - `TheStats_AreTheClassAtLevelOne` : après le choix, `GetStatsAsync().MaxHealth == 120`, une source `class`.
  - `AStoredCharacter_IsHydrated` : une ligne insérée (niveau 7) puis un grain neuf → `GetSummaryAsync().Level == 7`.
  - `AStoredClassUnknownToTheContent_StaysReadable` : ligne avec `class.paladin` → `GetSummaryAsync()` non nul, `GetStatsAsync()` à stats de classe nulles, un avertissement journalisé, aucune exception.
- [ ] **Step 2: Vérifier l'échec** · Expected: compilation.
- [ ] **Step 3: Implémenter.** `PlayerCharacterGrain(IDbContextFactory<VortexDbContext>, IRpgContent, IEnumerable<IStatSource>, IEventPublisher, IOptions<CharacterConfig>, ILogger<PlayerCharacterGrain>)` :
  - `OnActivateAsync` : lit la ligne `AsNoTracking` par `PlayerEntityId` (absente = pas de personnage) ; `RegisterGrainTimer` de `FlushIntervalMs` qui appelle `FlushAsync` (comme `RoomPersistenceGrain`).
  - `ChooseClassAsync` : ligne déjà là → `AlreadyChosen` ; `content.Get<ClassDefinition>(id)` null ou `Retired` → `UnknownClass` ; sinon insertion immédiate (niveau 1, XP 0, `Specializations[0]`), log `Character created for player {PlayerId}: {ClassId}`, `CharacterCreatedEvent` par `LogAndForget`.
  - `GetStatsAsync` : null sans personnage ; sinon instantané en cache, recalculé quand `content.Version` diffère de la version du cache : contributions = `await source.ContributeAsync(new StatSourceContext(id, classe, niveau), ct)` pour chaque source, puis `StatCalculator.Compute(…, progression, classe?.PrimaryStat)`.
  - `FlushAsync` : rien si non sale ; sinon `ExecuteUpdateAsync` de `Level`, `Xp`, `LastRoomId` seulement (jamais `ClassId`/`SpecializationId`) ; en cas d'échec, reste sale et journalise (`LogWarning(ex, …)`). `OnDeactivateAsync` appelle `FlushAsync`.
  - `ClassLevelStatSource` (`Name => "class"`) : `Class` null → `StatBlock` vide ; sinon `base + perLevel × (level − 1)` champ par champ.
  - Ajouter `Rpg.Characters` à `PLATFORM`, le module dans `Program.cs`, le projet à la solution.
- [ ] **Step 4: Vérifier le succès** — `dotnet test Vortex.Database.Tests --filter PlayerCharacterGrainTests` · Expected: PASS (7) ; `node scripts/hooks/check-architecture-walls.mjs` · Expected: aucune fuite.
- [ ] **Step 5: Commit** — `rpg : grain du personnage, choix de la classe et stats`.

---

### Task 4: L'XP et la montée de niveau

**Files:**
- Create: `Vortex.Primitives/Rpg/Characters/XpGrantOutcome.cs`, `Vortex.Primitives/Events/Rpg/XpGainingEvent.cs`, `XpGainedEvent.cs`, `LevelUpEvent.cs`
- Modify: `IPlayerCharacterGrain.cs` (`GainXpAsync`), `PlayerCharacterGrain.cs`
- Test: `Vortex.Database.Tests/Rpg/PlayerCharacterXpTests.cs`

**Interfaces:**
- Produces:
  - `Task<XpGrantOutcome> GainXpAsync(int amount, string source)` ; `enum XpGrantOutcome { Granted = 0, Refused = 1, NoCharacter = 2, Cancelled = 3, AtMaxLevel = 4 }`.
  - `sealed record XpGainingEvent(PlayerId PlayerId, string Source) : IEvent { public required int Amount { get; set; } }` — le montant est modifiable par un comportement (`IEventBehavior<XpGainingEvent>`, chaînés ; les handlers tournent en parallèle et ne doivent pas l'écrire).
  - `record XpGainedEvent(PlayerId PlayerId, int Amount, string Source, int Level, int Xp) : IEvent` ; `record LevelUpEvent(PlayerId PlayerId, string ClassId, int OldLevel, int NewLevel) : IEvent`.

- [ ] **Step 1: Écrire les tests** (faux `ICancellableEventPublisher` par `FakeProxy` qui renvoie un `EventContext` réglable et peut changer `Amount`) :
  - `AGrant_AddsXpWithoutWriting` : 50 XP → `Granted`, résumé `Xp == 50`, la ligne garde `xp = 0` (pas d'écriture par gain).
  - `ReachingTheThreshold_LevelsUpAndWritesAtOnce` : 100 XP au niveau 1 → niveau 2, XP 0, ligne `level = 2` écrite sans vidage, un `LevelUpEvent(1 → 2)`.
  - `AGrantOverSeveralLevels_StopsAtTheMax` (Review Focus 4) : 10⁵ XP au niveau 1 avec `MaxXpPerGrant = 10⁵` → niveau 20, XP 0, 19 `LevelUpEvent`, une seule écriture.
  - `AtTheMaxLevel_XpDoesNotAccumulate` : niveau 20 → `AtMaxLevel`, XP 0.
  - `ACancelledGain_ChangesNothing` : contexte `Cancel = true` → `Cancelled`, XP inchangée, pas de `XpGainedEvent`.
  - `AModifiedAmount_IsTheOneGranted` : le comportement passe `Amount` de 50 à 80 → XP 80.
  - `ZeroNegativeOrOverTheCap_IsRefusedAndLogged` : 0, −5, `MaxXpPerGrant + 1` → `Refused`.
  - `WithoutACharacter_NothingHappens` → `NoCharacter`.
  - `AFlush_WritesTheXp` : 50 XP puis `OnDeactivateAsync` → ligne `xp = 50`.
- [ ] **Step 2: Vérifier l'échec** · Expected: compilation.
- [ ] **Step 3: Implémenter** `GainXpAsync` : pas de personnage → `NoCharacter` ; `amount` ∉ [1, `MaxXpPerGrant`] → log + `Refused` ; niveau = max → `AtMaxLevel` ; `PublishCancellableAsync(new XpGainingEvent(id, source) { Amount = amount })`, `Cancel` → `Cancelled`, montant relu (≤ 0 → `Refused`) ; ajouter, puis tant que niveau < max et XP ≥ `XpToNext[niveau − 1]` : soustraire, monter, `LevelUpEvent` + log `Level up {PlayerId} {ClassId} {Level}` ; au max, XP = 0. Montée → stats invalidées, `CharacterStatsChangedEvent`, écriture immédiate (`FlushAsync` forcé) ; sinon marquer sale. Puis `XpGainedEvent`. Les events « -ed » par `LogAndForget`. Injecter `ICancellableEventPublisher` (ajouter au constructeur ; mettre à jour `CreateWithIntegerKey<PlayerCharacterGrain>` dans les tests de la Tâche 3).
- [ ] **Step 4: Vérifier le succès** · Expected: PASS (9), et les tests de la Tâche 3 toujours verts.
- [ ] **Step 5: Commit** — `rpg : gain d'XP et montée de niveau`.

---

### Task 5: Les références de contenu et le contenu qui change

**Files:**
- Create: `Vortex.Rpg.Characters/Content/ClassReferences.cs`, `SpecializationReferences.cs`
- Modify: `CharactersModule.cs` (`AddSingleton<IContentReferences, ClassReferences>()` et `SpecializationReferences`), `PlayerCharacterGrain.cs`
- Test: `Vortex.Database.Tests/Rpg/CharacterContentReferencesTests.cs`

**Interfaces:**
- Consumes: `IContentReferences` (`Kind`, `IsReferencedAsync(db, id, ct)`, `ReplaceAsync(db, from, to, ct)`), `RpgContentService` (import).
- Produces: `ClassReferences.Kind == "class"` (colonne `class_id`), `SpecializationReferences.Kind == "specialization"` (colonne `specialization_id`), par `AnyAsync` / `ExecuteUpdateAsync` dans la transaction de l'import.

- [ ] **Step 1: Écrire les tests** :
  - `RemovingAHeldClass_WithoutARule_IsRefused` : un personnage `class.warrior`, import sans la classe → `ReferencedWithoutMigration`.
  - `AReplaceRule_MovesTheCharacters` : règle `class.warrior → class.knight` (avec `spec.warrior.guardian → spec.knight.guardian`) → `class_id` et `specialization_id` réécrits.
  - `AReplacedClass_IsReReadAndNeverWrittenBack` (Review Focus 3) : grain actif sur `class.warrior`, XP accordée (sale), import `replace` → `GetSummaryAsync().ClassId == "class.knight"` ; `OnDeactivateAsync` → la ligne reste `class.knight`.
- [ ] **Step 2: Vérifier l'échec** · Expected: les deux premiers échouent (aucune référence déclarée : l'import passe), le troisième sur la classe relue.
- [ ] **Step 3: Implémenter** les deux références ; dans le grain, à chaque lecture (`GetSummaryAsync`, `GetStatsAsync`, `GainXpAsync`) : si `content.Version` a changé depuis la dernière lecture, relire `class_id` et `specialization_id` de la ligne (une requête) puis invalider les stats.
- [ ] **Step 4: Vérifier le succès** · Expected: PASS (3).
- [ ] **Step 5: Commit** — `rpg : les personnages suivent les migrations de classe`.

---

### Task 6: La dernière zone et la connexion

**Files:**
- Create: `Vortex.Rpg.Characters/Events/LastZoneHandler.cs` (public : découvert par balayage)
- Modify: `IPlayerCharacterGrain.cs` (`SetLastRoomAsync`, `GetLastRoomAsync`), `PlayerCharacterGrain.cs`, `Vortex.PacketHandlers/Handshake/SSOTicketMessageHandler.cs` (+ `IZoneRoomSource`), `Vortex.PacketHandlers.Tests/SsoBurstTests.cs`
- Test: `Vortex.Database.Tests/Rpg/LastZoneTests.cs`, `Vortex.PacketHandlers.Tests/SsoBurstTests.cs`

**Interfaces:**
- Produces: `Task SetLastRoomAsync(RoomId roomId)` (ignoré sans personnage ou si `ZoneInstanceIds.IsInstance(roomId)` ; marque sale) ; `Task<RoomId?> GetLastRoomAsync()`.
- `LastZoneHandler(IGrainFactory) : IEventHandler<PlayerEnteredRoomEvent>` → `GetPlayerCharacterGrain(e.PlayerId).SetLastRoomAsync(new RoomId(e.RoomId))`.

- [ ] **Step 1: Écrire les tests** :
  - `LastZoneTests.AWorldZone_IsKept` / `ADungeonCopy_IsNeverKept` (id ≥ 1 000 000 000) / `WithoutACharacter_NothingIsKept`.
  - `SsoBurstTests.TheLastZone_IsWhereTheCharacterSignsIn` : grain → room 12, `IZoneRoomSource.GetPublishedAsync(12)` non nul → `RoomForward` 12.
  - `SsoBurstTests.ALastZoneNoLongerPublished_FallsBackToTheStart` (Review Focus 2) : `GetPublishedAsync` null → `RoomForward` vers `world.start_room_id`.
  - `SsoBurstTests.ATestEntry_StillComesFirst` : entrée de test 30 et dernière zone 12 → 30.
- [ ] **Step 2: Vérifier l'échec** · Expected: compilation puis échecs d'assertion.
- [ ] **Step 3: Implémenter** ; dans le handler SSO, ajouter `GetPlayerCharacterGrain(playerId).GetLastRoomAsync()` au `Task.WhenAll` existant, puis l'ordre : test → dernière zone si `await zoneSource.GetPublishedAsync(room, ct) is not null` → départ. Mettre à jour la construction du handler dans `SsoBurstTests` (constructeur changé).
- [ ] **Step 4: Vérifier le succès** — `dotnet test Vortex.PacketHandlers.Tests` et le filtre `LastZoneTests` · Expected: PASS.
- [ ] **Step 5: Commit** — `rpg : retour dans la dernière zone à la connexion`.

---

### Task 7: Le délai de grâce

**Files:**
- Modify: `Vortex.Players/Configuration/PlayerPresenceConfig.cs` (`DisconnectGraceSeconds = 60`), `Vortex.Players/Grains/PlayerPresenceGrain.cs`, `PlayerPresenceGrain.Room.cs`, `Vortex.Primitives/Players/Grains/IPlayerPresenceGrain.Room.cs` (`GetResumableRoomAsync`, `TakeResumeAsync`), `Vortex.Primitives/Rooms/Grains/IRoomAvatars.cs` + `Vortex.Rooms/Grains/RoomGrain.Avatar.cs` (`StopAvatarAsync`), `Vortex.Rooms/RoomService.cs` (pas de `ClearActiveRoomAsync` sur une reprise), `SSOTicketMessageHandler.cs` (la room en grâce passe avant la dernière zone)
- Test: `Vortex.Players.Tests/Presence/DisconnectGraceTests.cs`, `Vortex.Rooms.Tests/…/RoomResumeTests.cs` (harnais `RoomHarness`), `SsoBurstTests.cs`

**Interfaces:**
- Produces:
  - `IPlayerPresenceGrain.GetResumableRoomAsync() : Task<RoomId?>` — la room d'une session rattachée pendant la grâce, sinon null.
  - `IPlayerPresenceGrain.TakeResumeAsync(RoomId roomId) : Task<bool>` — vrai une fois si `roomId` est cette room, puis oublié.
  - `IRoomAvatars.StopAvatarAsync(ActionContext ctx, PlayerId playerId, CancellationToken ct) : Task<bool>` → `RoomAvatarModule.StopWalkingAsync` de l'avatar du joueur.

- [ ] **Step 1: Écrire les tests** :
  - `DisconnectGraceTests.ACutWhileInARoom_KeepsTheAvatar` : session enregistrée, room active 7, `UnregisterSessionObserverAsync` → aucun `RemoveAvatarFromPlayerAsync` sur la room, un `StopAvatarAsync`, la grain retient sa désactivation (`DelayDeactivation`).
  - `…ReconnectingWithinGrace_ResumesTheRoom` : nouvelle session avant le minuteur → `GetResumableRoomAsync() == 7`, minuteur arrêté.
  - `…TheGraceEnding_LeavesTheRoom` : appel direct du rappel du minuteur (les minuteurs de test ne tournent pas) → `RemoveAvatarFromPlayerAsync` et `PlayerLeftRoomEvent`.
  - `…OutsideARoom_ThereIsNoGrace` (le chemin `Quit` : la room est déjà quittée) → sortie immédiate comme aujourd'hui.
  - `…ADeactivation_LeavesAtOnce` : `OnDeactivateAsync` pendant la grâce → sortie.
  - `RoomResumeTests.AReconnectWithinGrace_KeepsTheOneAvatar` (Review Focus 1) : avatar en marche, coupure (`StopAvatarAsync`), reprise (`TakeResumeAsync` vrai) puis entrée → un seul avatar pour ce joueur, même case.
  - `SsoBurstTests.ARoomInGrace_ComesBeforeTheLastZone`.
- [ ] **Step 2: Vérifier l'échec** · Expected: compilation puis assertions.
- [ ] **Step 3: Implémenter.** Dans `UnregisterSessionObserverAsync` (appelé par la passerelle à la fermeture de session) : si `_state.ActiveRoomId > 0` et `DisconnectGraceSeconds > 0` → `_sessionObserver = null`, `StopAvatarAsync` sur la room (contexte construit comme dans `ClearActiveRoomAsync`), `RegisterGrainTimer` à `DisconnectGraceSeconds` (une fois) qui appelle `EndGraceAsync`, `DelayDeactivation(grâce + 10 s)`, puis retour ; sinon comportement actuel. `OnDeactivateAsync` passe par une sortie immédiate (extraire la sortie actuelle en `LeaveNowAsync`). `RegisterSessionObserverAsync` : minuteur en cours → le disposer et retenir `_resumableRoom = _state.ActiveRoomId`. `EndGraceAsync` : si aucune session n'est revenue, `LeaveNowAsync` et `DeactivateOnIdle`. `RoomService.OpenRoomForPlayerIdAsync` : `if (!await playerPresence.TakeResumeAsync(roomId)) await playerPresence.ClearActiveRoomAsync(ct)` ; `SetActiveRoomAsync` garde déjà l'avatar quand la room est la même. Ajouter les nouvelles méthodes à `interleaving-manifest.yaml` seulement si elles portent un attribut d'entrelacement (elles n'en portent pas).
- [ ] **Step 4: Vérifier le succès** — `dotnet test Vortex.Players.Tests`, `Vortex.Rooms.Tests --filter RoomResumeTests`, `Vortex.PacketHandlers.Tests` · Expected: PASS ; `grain-rules-reviewer` sur le diff de `Vortex.Players/Grains` · Expected: aucun constat bloquant.
- [ ] **Step 5: Commit** — `joueurs : 60 s de grâce à la coupure, reprise sans double avatar`.

---

### Task 8: Les routes du web API

**Files:**
- Create: `Vortex.Primitives/Rpg/Content/ClassSummary.cs`, `Vortex.WebApi/Http/ChooseClassRequest.cs`, `Vortex.WebApi/Hosting/WebApiEndpoints.Characters.cs`
- Modify: `Vortex.Primitives/Rpg/Content/IRpgContentService.cs` (`GetClasses()`), `Vortex.Rpg/Content/Import/RpgContentService.cs`, `Vortex.WebApi/Http/WebApiRequests.cs` (`CreateAvatarRequest.ClassId`), `Vortex.WebApi/Services/IWebApiPlayerService.cs` (`AvatarInfo.ClassId`, `AvatarInfo.Level`, `CreateAvatarAsync(…, string? classId, …)`), `Vortex.WebApi/Services/WebApiPlayerService.cs`, `Vortex.WebApi/Hosting/WebApiEndpoints.cs` (création, `MapCharacters`), `Vortex.WebApi.Tests/Fakes.cs`, `FakeRpgContentService.cs`, `Vortex.WebApi/openapi.json`, `scripts/hooks/authorization-surface-baseline.json`, `.claude/skills/run-vortex-emulator/driver.mjs` (création avec `classId: "class.warrior"`)
- Test: `Vortex.WebApi.Tests/CharacterEndpointsTests.cs`

**Interfaces:**
- Produces:
  - `[GenerateSerializer, Immutable] sealed record ClassSummary([property: Id(0)] string Id, [property: Id(1)] string Role, [property: Id(2)] int Range, [property: Id(3)] string PrimaryStat, [property: Id(4)] IReadOnlyList<string> Specializations)` ; `IRpgContentService.GetClasses() : IReadOnlyList<ClassSummary>` (classes non retirées, ordre des ids, rôle et stat en snake_case comme le JSON).
  - `GET /api/public/content/classes` → 200 `ClassSummary[]`.
  - `POST /api/user/avatars` : `classId` requis si `GetClasses()` n'est pas vide (400 `avatar.class_required`), inconnu → 400 `avatar.unknown_class` (vérifié **avant** de créer le joueur) ; après création, `GetPlayerCharacterGrain(id).ChooseClassAsync(classId)`.
  - `POST /api/user/avatars/{uniqueId}/class` (`ChooseClassRequest(string? ClassId)`) : 401 sans session, 403 `avatar_not_owned`, 400 `avatar.unknown_class`, 409 `avatar.class_already_chosen`, 200 sinon.
  - `GET /api/user/avatars` : `ClassId`, `Level` (null sans personnage), par `GetSummaryAsync` en parallèle (`Task.WhenAll`).

- [ ] **Step 1: Écrire les tests** : classes listées ; création sans `classId` → 400 quand des classes existent, avec → 200 et la classe choisie ; classe inconnue → 400 sans joueur créé ; choix 200 puis 409 ; avatar d'un autre compte → 403 ; liste avec `classId` / `level`, et null pour un avatar sans personnage (Review Focus 5).
- [ ] **Step 2: Vérifier l'échec** · Expected: compilation.
- [ ] **Step 3: Implémenter** ; `UPDATE_OPENAPI=1 dotnet test Vortex.WebApi.Tests --filter OpenApiDocumentTests` ; `node scripts/hooks/check-authorization-surface.mjs --update` (relire les entrées ajoutées) ; `node scripts/hooks/check-webapi-forwarding.mjs` · Expected: OK.
- [ ] **Step 4: Vérifier le succès** — `dotnet test Vortex.WebApi.Tests` · Expected: PASS.
- [ ] **Step 5: Commit** — `rpg : classe à la création d'un avatar, routes du personnage`.

---

### Task 9: XP de test, observabilité

**Files:**
- Create: `Vortex.WebApi/Http/GrantXpRequest.cs`
- Modify: `Vortex.Primitives/Permissions/Capabilities.cs` (classe `Rpg` : `GrantXp = "rpg.character.grant_xp"`, dans `All`), `WebApiEndpoints.Characters.cs` (`POST /api/admin/characters/{playerId}/xp`), `Vortex.Primitives/Observability/IVortexMetrics.cs` + `Vortex.Observability/Metrics/VortexMetrics.cs` (`CharacterActivated(int delta)` → compteur montant-descendant `Vortex.character.active` ; `CharacterFlushCompleted(double elapsedMilliseconds, bool failed)` → histogramme `Vortex.character.flush.duration`, tag `outcome` = `ok` | `failed`), `PlayerCharacterGrain.cs`, `.claude/skills/run-vortex-emulator/driver.mjs` (commande `character`), `SKILL.md`
- Test: `Vortex.WebApi.Tests/CharacterEndpointsTests.cs` (route d'XP : 403 sans la capability, 200 avec et `XpGrantOutcome` rendu)

- [ ] **Step 1–4:** tests de la route (le faux `IGrainFactory` de `WebApiTestFactory` rend un faux `IPlayerCharacterGrain`), implémentation, métriques appelées à l'activation (+1), la désactivation (−1) et chaque vidage ; `driver.mjs character` : crée (ou réutilise) l'avatar Guerrier, connecte, `GET /api/user/avatars` (niveau 1), `POST /api/admin/characters/{id}/xp` 250 (compte jetable promu `owner` comme `zone`), relit le niveau (3).
- [ ] **Step 5: Commit** — `rpg : XP de test et métriques du personnage`.

---

### Task 10: Preuve de bout en bout et documentation

**Files:**
- Modify: `docs/architecture.md` (flux 7 « Personnage », tables `player_characters`), `AGENTS.md` (ligne « An RPG system » → `Vortex.Rpg.Characters` en exemple)

- [ ] **Step 1: Gate** — `dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudQualityGate` (redirigé vers un fichier) · Expected: 0 erreur, tous les tests verts.
- [ ] **Step 2: En vrai** sur `ambreia-database` (republier le plugin web) : `driver.mjs character` · Expected: niveau 1 puis 3 ; la ligne `player_characters` porte `level = 3`.
- [ ] **Step 3: Dernière zone** — `driver.mjs travel` (il finit dans `proof_b`), puis `driver.mjs play` · Expected: `RoomForward` vers `proof_b`, pas la zone de départ.
- [ ] **Step 4: Grâce** — étendre `driver.mjs` d'une commande `grace` : entrer, marcher, fermer la socket, se reconnecter en moins de 60 s · Expected: `RoomForward` vers la même room, `Users` avec **un** avatar du joueur, sur sa case ; puis attendre plus de 60 s sans revenir · Expected: le log annonce la sortie.
- [ ] **Step 5: Docs et commit** — `docs : personnage RPG`.
