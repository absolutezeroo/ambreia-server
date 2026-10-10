# Zones Z1 « Règles et déclencheurs » — plan d'implémentation

> **Pour l'exécutant :** skill `executing-a-vortex-plan` (Claude implémente chaque tâche, un relecteur
> toutes les deux tâches). Les étapes sont des cases à cocher.

**But :** tout ce que l'éditeur écrit dans les déclencheurs et les points d'apparition, et que les
systèmes actuels savent faire, marche en jeu.

**Architecture :** les conditions et actions deviennent des pièces déclarées par DI (contrats dans
`Vortex.Rpg/Zones/Rules`) à côté des pièces propres aux rooms ; la zone tient les faits de chaque
joueur (niveau, objets) poussés par son personnage ; la simulation des monstres signale ses morts à
la room et gère sommeil, vagues, conditions et proximité de ses points.

**Pile :** C# / .NET 10, Orleans, xUnit + FluentAssertions, `FakeProxy`.

**Spec :** `docs/superpowers/specs/2026-10-10-zone-rules-z1-design.md`

## Contraintes globales

- Un type par fichier ; le namespace est le dossier ; csharpier après chaque édition (hook).
- `[Id(n)]` jamais renuméroté ; un nouveau champ Orleans prend le numéro suivant.
- Les pièces sont des singletons sans état : l'état vit dans la zone (`ZoneRuntimeState`,
  `MonsterSpawner`).
- Jamais d'appel de grain attendu depuis le personnage vers la room (la room peut attendre ce
  personnage) : `LogAndForget`, comme `TellTheZone`.
- Configuration `Vortex:Monsters` : `AoiRadius` 12, `AoiSleepSeconds` 30, `SpawnConditionCheckMs` 1000.
- Messages au joueur en français, par `ZoneBubble` (2243), type `zone.level` pour le niveau.
- Commits en français, chemins explicites (`git commit -o`).

## Points d'attention de relecture

1. Un joueur qui entre sans personnage (compte sans classe) : pas de faits, niveau et objet valent
   faux, aucun déclencheur niveau/objet, aucune exception — test en tâche 2.
2. Un relevé de faits qui arrive pour un joueur déjà parti de la room : ignoré, pas recréé — tâche 2.
3. Un monstre tué par un joueur qui a quitté la room avant la fin du tick : pas de déclencheur pour
   lui — tâche 5.
4. Un point en sommeil réveillé alors que sa zone n'a aucun joueur (horloge) : la vague apparaît
   au tick suivant où un joueur est là, pas avant — tâche 6.
5. Des conditions de point d'apparition qui redeviennent fausses pendant un combat : le monstre en
   combat reste — tâche 7.

---

### Tâche 1 : règles déclarées et registre des rooms

**Fichiers :**
- Créer : `Vortex.Primitives/World/Rules/IZoneRuleDeclaration.cs`, `Vortex.Primitives/World/Rules/DeclaredZoneRule.cs`
- Créer : `Vortex.Rpg/Zones/Rules/IZoneConditionEvaluator.cs`, `IZoneActionExecutor.cs`, `ZoneConditionEvaluator.cs`, `ZoneActionExecutor.cs`, `ZoneRuleContext.cs`, `ZoneRuleServices.cs`
- Renommer dans `Vortex.Rooms/Grains/Zones/Triggers/` : `IZoneConditionEvaluator` → `IRoomConditionEvaluator`, `ZoneConditionEvaluator<T>` → `RoomConditionEvaluator<T>`, `IZoneActionExecutor` → `IRoomActionExecutor`, `ZoneActionExecutor<T>` → `RoomActionExecutor<T>` (fichiers renommés, les 2 évaluateurs et 6 exécuteurs suivent)
- Remplacer : `Triggers/Conditions/ZoneConditions.cs` et `Triggers/Actions/ZoneActions.cs` par `Vortex.Rooms/Grains/Zones/Triggers/ZoneRuleRegistry.cs` (+ `RoomRules.cs` : les pièces des rooms)
- Modifier : `ZoneTriggerContext.cs` (ajoute `Facts`, `ToRuleContext()`), `ZoneTriggerModule.cs`, `RoomGrain.cs` (ctor : `ZoneRuleRegistry rules`), `RoomModule.cs`, `Vortex.Rooms.Tests/Support/RoomHarness.cs`
- Test : `Vortex.Rooms.Tests/Zones/ZoneRuleRegistryTests.cs`

**Interfaces :**
- Produit :
  ```csharp
  // Vortex.Primitives.World.Rules
  public interface IZoneRuleDeclaration { Type RuleType { get; } }
  public sealed record DeclaredZoneRule(Type RuleType) : IZoneRuleDeclaration;

  // Vortex.Rpg.Zones.Rules
  public sealed record ZoneRuleContext(PlayerId PlayerId, CharacterZoneFacts? Facts,
      DateTimeOffset LocalNow, IZoneRuntime Runtime, IZoneSimulation Simulation);
  public interface IZoneConditionEvaluator : IZoneRuleDeclaration
  { bool Holds(ZoneCondition condition, ZoneRuleContext ctx); }
  public interface IZoneActionExecutor : IZoneRuleDeclaration
  { Task RunAsync(ZoneAction action, ZoneRuleContext ctx, CancellationToken ct); }
  public abstract class ZoneConditionEvaluator<T> : IZoneConditionEvaluator where T : ZoneCondition
  { public Type RuleType => typeof(T); protected abstract bool Holds(T c, ZoneRuleContext ctx); }
  public abstract class ZoneActionExecutor<T> : IZoneActionExecutor where T : ZoneAction
  { public Type RuleType => typeof(T); protected abstract Task RunAsync(T a, ZoneRuleContext ctx, CancellationToken ct); }
  public static class ZoneRuleServices
  {   // déclare la pièce et sa déclaration (une instance, vue deux fois)
      public static IServiceCollection AddZoneCondition<T>(this IServiceCollection s) where T : class, IZoneConditionEvaluator;
      public static IServiceCollection AddZoneAction<T>(this IServiceCollection s) where T : class, IZoneActionExecutor;
  }

  // Vortex.Rooms.Grains.Zones.Triggers
  public sealed class ZoneRuleRegistry   // singleton DI
  {
      public ZoneRuleRegistry(IEnumerable<IZoneConditionEvaluator> conditions,
          IEnumerable<IZoneActionExecutor> actions, ILogger<ZoneRuleRegistry> logger);
      public bool AllHold(IEnumerable<ZoneCondition> conditions, ZoneTriggerContext ctx);
      public Task RunAsync(ZoneAction action, ZoneTriggerContext ctx, CancellationToken ct);
  }
  ```
  `ZoneTriggerContext` gagne `CharacterZoneFacts? Facts` (dernier paramètre, `null` jusqu'à la tâche 2)
  et `ZoneRuleContext ToRuleContext() => new(PlayerId, Facts, LocalNow, Room.CombatRuntime, Room.Simulation)`.
- `CharacterZoneFacts` est créé ici (tâche 2 le remplit) :
  ```csharp
  // Vortex.Primitives/Rpg/Characters/CharacterZoneFacts.cs
  [GenerateSerializer, Immutable, Alias("Vortex.Primitives.Rpg.Characters.CharacterZoneFacts")]
  public sealed record CharacterZoneFacts
  {
      [Id(0)] public required int Level { get; init; }
      /// <summary>Définition → quantité, sac et équipement (pas les colis).</summary>
      [Id(1)] public required ImmutableDictionary<string, int> Items { get; init; }
  }
  ```

- [ ] **Étape 1 : tests rouges** (`ZoneRuleRegistryTests`)

```csharp
[Fact]
public async Task AConditionDeclaredByAModule_IsAnswered()
{
    // Une pièce RPG de test répond vrai pour PlayerLevel (aucune pièce des rooms ne le connaît).
    RoomHarness harness = await RoomHarness.CreateZoneAsync(
        ZoneSamples.Minimal(),
        rules: [new AlwaysTrue<PlayerLevelCondition>()]);
    harness.Grain.ZoneTriggers
        .ConditionsHold([new PlayerLevelCondition { Min = 1, Max = 0 }], new PlayerId(7))
        .Should().BeTrue();
}

[Fact]
public async Task AConditionNobodyDeclared_IsFalse()
{
    RoomHarness harness = await RoomHarness.CreateZoneAsync(ZoneSamples.Minimal());
    harness.Grain.ZoneTriggers
        .ConditionsHold([new HasItemCondition { Item = "item.x", Count = 1 }], new PlayerId(7))
        .Should().BeFalse();
}

[Fact]
public void ARuleDeclaredTwice_TheLastWins()
{
    ZoneRuleRegistry registry = new(
        [new AlwaysFalse<PlayerLevelCondition>(), new AlwaysTrue<PlayerLevelCondition>()],
        [], NullLogger<ZoneRuleRegistry>.Instance);
    registry.AllHold([new PlayerLevelCondition { Min = 1 }], RoomHarness.BareContext())
        .Should().BeTrue();
}

[Fact]
public async Task AnActionDeclaredByAModule_Runs()
{
    RecordingAction<SpawnGroupAction> spawn = new();
    RoomHarness harness = await RoomHarness.CreateZoneAsync(TriggerOnTimer(new SpawnGroupAction { Spawn = "g" }),
        actions: [spawn]);
    await harness.AdvanceClockAsync(seconds: 2);
    spawn.Runs.Should().ContainSingle();
}

[Fact]
public async Task TheRoomsOwnRules_StillWork()   // TimeOfDay, ZoneActive, ShowDialogue : déjà couverts
{                                                // par ZoneTriggerTests — ils doivent rester verts.
}
```
`AlwaysTrue<T>`, `AlwaysFalse<T>`, `RecordingAction<T>` : petites pièces de test dans
`Vortex.Rooms.Tests/Support/` (un type par fichier). `RoomHarness.CreateZoneAsync` prend
`IZoneConditionEvaluator[]? rules = null, IZoneActionExecutor[]? actions = null` et construit le
`ZoneRuleRegistry` passé au grain. `RoomHarness.BareContext()` : un `ZoneTriggerContext` de la room
minimale, joueur 0.

- [ ] **Étape 2 :** `dotnet test Vortex.Rooms.Tests --filter "FullyQualifiedName~ZoneRuleRegistryTests"` → échec de compilation (types absents).
- [ ] **Étape 3 : implémenter.**
  - `ZoneRuleRegistry` : dictionnaires `Type → pièce`. D'abord les pièces des rooms
    (`RoomRules.Conditions`, `RoomRules.Actions` : les 2 évaluateurs et 6 exécuteurs actuels), puis
    celles de DI ; une clé déjà prise par DI → avertissement « {Type} declared twice; the last one
    wins » et remplacement. `AllHold` : pour chaque condition, pièce des rooms →
    `Holds(c, ctx)` ; pièce DI → `Holds(c, ctx.ToRuleContext())` ; aucune → faux. `RunAsync` : idem ;
    aucune → `LogDebug` comme aujourd'hui.
  - `RoomRules` expose aussi `public static IEnumerable<Type> Shipped` (les 8 types) ; `RoomModule`
    déclare `services.AddSingleton<IZoneRuleDeclaration>(new DeclaredZoneRule(t))` pour chacun, puis
    `services.AddSingleton<ZoneRuleRegistry>()`.
  - `ZoneTriggerModule` appelle `roomGrain._zoneRules.AllHold` / `RunAsync` au lieu des statiques.
  - Garder le commentaire de `ZoneActions` (« sans pièce : journalisée et sautée ») sur `RunAsync`.
- [ ] **Étape 4 :** tests de la tâche + `ZoneTriggerTests`, `ZonePassageTests`, `ZoneAreaTests` verts.
- [ ] **Étape 5 : commit** `zones : conditions et actions déclarées par leur module`.

---

### Tâche 2 : les faits du joueur dans la zone

**Fichiers :**
- Modifier : `Vortex.Primitives/Rpg/Characters/Grains/IPlayerCharacterGrain.cs` (+ `Task<CharacterZoneFacts?> GetZoneFactsAsync();`)
- Modifier : `Vortex.Primitives/Players/Grains/IPlayerPresenceGrain.Avatar.cs` (+ `Task OnCharacterFactsChangedAsync(CharacterZoneFacts facts, CancellationToken ct);`)
- Modifier : `Vortex.Primitives/Rooms/Grains/IRoomZone.cs` (+ `Task UpdatePlayerFactsAsync(PlayerId player, CharacterZoneFacts facts);`)
- Créer : `Vortex.Rpg.Characters/Grains/PlayerCharacterGrain.Zone.cs` (`GetZoneFactsAsync`, `TellTheZoneFacts()`)
- Modifier : `PlayerCharacterGrain.Items.cs`, `.Progress.cs`, `.Class.cs` (appels à `TellTheZoneFacts()`)
- Modifier : `Vortex.Players/Grains/PlayerPresenceGrain.Avatar.cs` (relais, non attendu)
- Créer : `Vortex.Rooms/Grains/RoomGrain.Facts.cs` (`UpdatePlayerFactsAsync`)
- Modifier : `Vortex.Rooms/Grains/Zones/Runtime/ZoneRuntimeState.cs` (+ `Dictionary<PlayerId, CharacterZoneFacts> Facts`)
- Modifier : `Vortex.Rooms/Grains/Modules/RoomAvatarModule.Combat.cs` (lecture à l'entrée, en parallèle de `PlayerEnteredAsync`), `RoomAvatarModule.cs:121` (retrait au départ)
- Modifier : `ZoneTriggerModule.ContextFor` (passe `zone.Facts.GetValueOrDefault(playerId)`)
- Tests : `Vortex.Database.Tests/Rpg/CharacterZoneFactsTests.cs`, `Vortex.Rooms.Tests/Zones/ZoneFactsTests.cs`

**Interfaces :**
- Consomme : `CharacterZoneFacts` (tâche 1).
- Produit : `ZoneRuntimeState.Facts` ; `RoomGrain.UpdatePlayerFactsAsync` ; la tâche 4 s'accroche à
  cette méthode (ancien relevé disponible avant remplacement).

- [ ] **Étape 1 : tests rouges.**
  - `CharacterZoneFactsTests` (grain réel sur SQLite, comme `CharacterItemsTests`) :
    `Facts_CountBagAndEquipment_NotParcels` (2 potions au sac, 1 épée équipée, 1 colis → `{potion:2, epee:1}`),
    `Facts_CarryTheLevel`, `WithoutACharacter_FactsAreNull`,
    `AGrant_TellsTheZone` (une `IPlayerPresenceGrain` factice enregistre l'appel `OnCharacterFactsChangedAsync` après `GrantItemsAsync`),
    `ALevelUp_TellsTheZone`.
  - `ZoneFactsTests` (Rooms) :
    `OnEntry_TheRoomReadsTheFacts` (le faux grain de personnage répond `Level 4` → `zone.Facts[p].Level == 4`),
    `AnUpdate_ReplacesThem`, `OnExit_TheyAreForgotten`,
    `AnUpdateForAPlayerWhoLeft_IsIgnored` (point d'attention 2),
    `WithoutACharacter_NoFactsAndNoFault` (le faux répond `null` ; point d'attention 1).
- [ ] **Étape 2 :** les lancer → échec.
- [ ] **Étape 3 : implémenter.**
  - Personnage : `Facts(CharacterInventory inv, CharacterEntity c)` = `Level = c.Level`, `Items` =
    `inv.Live.Where(i => i.Container is Bag or Equipment).GroupBy(DefinitionId).Sum(Quantity)`.
    `TellTheZoneFacts()` : lit l'inventaire en mémoire (sans recharger s'il n'est pas chargé : alors
    `InventoryAsync()` une fois), appelle
    `grainFactory.GetPlayerPresenceGrain(_playerId).OnCharacterFactsChangedAsync(...)` en
    `LogAndForget`. Appelé : après une montée de niveau (bloc `c.Level != oldLevel`), dans
    `GrantItemsAsync` et `RemoveItemsAsync` après succès, dans `AnsweredAsync` quand `Ok` (destruction,
    colis réclamé, équiper), après un changement de classe (`Class.cs:124`).
  - Présence : si `_state.ActiveRoomId > 0` → `GetRoomZone(room).UpdatePlayerFactsAsync(PlayerId, facts)`
    en `LogAndForget`.
  - Room : `UpdatePlayerFactsAsync` : garde d'acteur (joueur > 0) ; zone absente ou joueur absent de
    `_state.PlayerAvatars` (ou équivalent) → rien ; sinon `zone.Facts[player] = facts`.
  - Entrée : `JoinFightsAsync` lance `GetPlayerCharacterGrain(player).GetZoneFactsAsync()` et
    `Simulation.PlayerEnteredAsync` ensemble (`Task.WhenAll`), range les faits non nuls. Une faute de
    lecture est journalisée et n'empêche pas l'entrée (même `catch` que l'existant).
- [ ] **Étape 4 :** tests verts.
- [ ] **Étape 5 : commit** `zones : la zone connaît le niveau et les objets de chaque joueur`.

---

### Tâche 3 : conditions niveau et objet possédé

**Fichiers :**
- Créer : `Vortex.Rpg.Characters/Zones/PlayerLevelEvaluator.cs`, `Vortex.Rpg.Characters/Zones/HasItemEvaluator.cs`
- Modifier : `Vortex.Rpg.Characters/CharactersModule.cs` (`services.AddZoneCondition<PlayerLevelEvaluator>(); services.AddZoneCondition<HasItemEvaluator>();`)
- Modifier : `Vortex.Rpg.Characters/Vortex.Rpg.Characters.csproj` si `Microsoft.Extensions.DependencyInjection` manque (déjà là via le module)
- Tests : `Vortex.Rpg.Tests/Zones/CharacterConditionsTests.cs` (le projet référence-t-il `Vortex.Rpg.Characters` ? sinon `Vortex.Database.Tests/Rpg/`) + un test branché dans `Vortex.Rooms.Tests/Zones/ZoneFactsTests.cs`

**Interfaces :** consomme `ZoneConditionEvaluator<T>`, `ZoneRuleContext.Facts`, `AddZoneCondition<T>`.

- [ ] **Étape 1 : tests rouges.**

```csharp
[Theory]
[InlineData(3, 2, 0, true)]   // max 0 : sans plafond
[InlineData(3, 2, 3, true)]
[InlineData(3, 4, 0, false)]
[InlineData(5, 2, 4, false)]
public void PlayerLevel(int level, int min, int max, bool holds) =>
    new PlayerLevelEvaluator()
        .Holds(new PlayerLevelCondition { Min = min, Max = max }, Ctx(level, []))
        .Should().Be(holds);

[Fact] public void PlayerLevel_WithoutFacts_IsFalse() => …Holds(…, Ctx(facts: null)).Should().BeFalse();

[Theory]
[InlineData(2, 2, true)]
[InlineData(1, 2, false)]
[InlineData(0, 0, false)] // count < 1 vaut 1
public void HasItem(int owned, int count, bool holds) => …

[Fact] public void HasItem_WithoutFacts_IsFalse() => …
```
Et, dans `ZoneFactsTests`, `ARestrictedArea_LetsInOnlyTheLevelItAsks` : aire `Restricted` avec
`PlayerLevel {min 2}`, évaluateur réel déclaré via `rules:` ; faits niveau 1 → `MayStepOnto` faux ;
mise à jour niveau 2 → vrai.
- [ ] **Étape 2 :** échec. **Étape 3 :** implémenter (`Math.Max(1, Count)`). **Étape 4 :** vert.
- [ ] **Étape 5 : commit** `personnages : conditions de zone niveau et objet possédé`.

---

### Tâche 4 : déclencheurs niveau atteint et objet obtenu

> **Révisée après la relecture des tâches 1–2 :** le noyau des rooms ne compare ni niveau ni objets.
> La source de la partie (`CharacterFactsSource.Changes`) dit ce qu'un changement déclenche ; les rooms
> publient un seul événement générique.

**Fichiers :**
- Créer : `Vortex.Primitives/Rooms/Events/Zone/ZoneTriggerRaisedEvent.cs` (record `PlayerEvent` : `ZoneTriggerType Type`, `string Target`)
- Modifier : `RoomGrain.Facts.cs` (après remplacement d'une partie : `_zoneRules.SourceOf(type)?.Changes(before, after)` → un `ZoneTriggerRaisedEvent` par signal), `ZoneTriggerModule.OnRoomEventAsync` (une branche)
- Modifier : `Vortex.Rpg.Characters/Zones/CharacterFactsSource.cs` (`Changes` : niveaux franchis, objets en hausse)
- Test : `Vortex.Rooms.Tests/Zones/ZoneFactTriggersTests.cs` (+ `Vortex.Database.Tests/Rpg/CharacterZoneConditionsTests.cs` pour `Changes`)

- [ ] **Étape 1 : tests rouges.** Une zone avec un déclencheur `PlayerLevel` cible `"3"` qui fait `ShowDialogue "lvl3"` et un `ItemOwned` cible `"item.key"` qui fait `ShowDialogue "key"` ; on lit les bulles envoyées (`harness.SentToPlayer`).
  - `ReachingTheLevel_FiresItsTrigger` (2 → 3 : une bulle `lvl3`) ;
  - `SkippingLevels_FiresEachOne` (1 → 4 : déclencheurs `2`, `3`, `4` ; seul `3` existe → une bulle) ;
  - `TheFirstReadingOnEntry_FiresNothing` (entrée au niveau 5) ;
  - `GainingTheItem_FiresItsTrigger` (`{}` → `{item.key:1}`) ; `LosingOrKeeping_FiresNothing` (2 → 1, 1 → 1).
- [ ] **Étape 2 :** échec. **Étape 3 :** implémenter : `CharacterFactsSource.Changes` donne pour `l`
  de `old.Level+1` à `new.Level` un `ZoneTriggerSignal(PlayerLevel, l)` et pour chaque objet dont
  `new > old` (absent = 0) un `ZoneTriggerSignal(ItemOwned, id)`. `UpdatePlayerFactsAsync`, avec une
  partie déjà tenue seulement, publie un `ZoneTriggerRaisedEvent` par signal. `ZoneTriggerModule` :
  `ZoneTriggerRaisedEvent e => FireAsync(e.Type, e.Target, e.PlayerId, ct)`.
- [ ] **Étape 4 :** vert. **Étape 5 : commit** `zones : déclencheurs niveau atteint et objet obtenu`.

---

### Tâche 5 : déclencheur monstre tué

**Fichiers :**
- Créer : `Vortex.Rpg.Combat/Zones/MonsterDeath.cs` : `public sealed record MonsterDeath(int ObjectId, string MonsterId, IReadOnlyList<PlayerId> Participants);`
- Modifier : `Vortex.Rpg.Combat/Zones/CombatZone.Deaths.cs` (`_died` devient `List<MonsterDeath>`), `CombatZone.cs:71` (`IReadOnlyList<MonsterDeath> DiedMonsters`)
- Modifier : `Vortex.Rpg/Zones/IZoneRuntime.cs` (+ `void RaiseTrigger(PlayerId player, ZoneTriggerType type, string target);` — générique, révisé après la relecture des tâches 1–2) et les trois `FakeZoneRuntime` (Combat, Monsters, Skills tests : enregistrent l'appel dans `Raised`)
- Modifier : `Vortex.Rpg.Monsters/Spawns/MonsterSpawner.cs` (`SpawnOf(objectId)` : l'id du point, celui du boss pour un renfort), `RpgZoneSimulation.TickAsync` (signale `MonsterKilled` point puis monstre pour chaque participant, puis `spawner.Died`)
- Modifier : `RoomZoneRuntime.cs` (file de signaux, vidée dans `FlushAsync` : un `ZoneTriggerRaisedEvent` (tâche 4) par signal dont le joueur est encore dans la room)
- Tests : `Vortex.Rpg.Monsters.Tests/MonsterKilledTests.cs`, `Vortex.Rooms.Tests/Zones/ZoneMonsterKilledTests.cs`

- [ ] **Étape 1 : tests rouges.**
  - Monstres : `ADeath_IsToldWithItsSpawnAndParticipants` (monde de test : un loup du point `wolves`
    tué → `Zone.Raised` contient `(joueur, MonsterKilled, "wolves")` puis `(joueur, MonsterKilled, "monster.forest.wolf")`) ;
    `ASummonsDeath_IsToldUnderItsBossSpawn`.
  - Rooms : `AKill_FiresTriggersOnTheSpawnAndOnTheMonster` (deux déclencheurs, cibles `wolves` et
    `monster.forest.wolf` → deux bulles) ; `OneRunPerParticipant` (2 joueurs → 2 bulles) ;
    `AParticipantWhoLeft_GetsNothing` (point d'attention 3).
  - Les tests existants qui lisent `DiedMonsters` (ids) passent à `.Select(d => d.ObjectId)`.
- [ ] **Étape 2 :** échec. **Étape 3 :** implémenter. Dans `RpgZoneSimulation.TickAsync` (bloc `finally`) :
  ```csharp
  foreach (MonsterDeath death in combat.DiedMonsters)
  {
      if (spawner.SpawnOf(death.ObjectId) is { } spawnId)
      {
          foreach (PlayerId p in death.Participants)
          {
              zone.RaiseTrigger(p, ZoneTriggerType.MonsterKilled, spawnId);
              zone.RaiseTrigger(p, ZoneTriggerType.MonsterKilled, death.MonsterId);
          }
      }
      spawner.Died(death.ObjectId, now);
  }
  ```
  (`SpawnOf` : `_live[id].Parent` → id du point du parent, sinon `Spawn.Source.Id`.)
  `RoomZoneRuntime.FlushAsync` publie, après les composers, un `ZoneTriggerRaisedEvent` par signal dont
  le joueur a encore un avatar dans la room ; une publication qui lève est journalisée, la file est
  vidée quand même.
- [ ] **Étape 4 :** `Vortex.Rpg.Combat.Tests`, `Vortex.Rpg.Monsters.Tests`, `Vortex.Rpg.Skills.Tests`, `Vortex.Rooms.Tests` verts.
- [ ] **Étape 5 : commit** `zones : déclencheur monstre tué`.

---

### Tâche 6 : effectif min..max, sommeil et vagues, action « faire apparaître un groupe »

**Fichiers :**
- Modifier : `Vortex.Rpg.Monsters/Spawns/SpawnPoint.cs` (+ `bool Dormant { get; init; }`, `int Target { get; set; }`, `HashSet<int> Wave { get; } = []`, `bool WaveDue { get; set; }`)
- Modifier : `MonsterSpawner.cs` (`Tick` : éveil → `Target = Random(min..max)` pour les points ordinaires ; points en sommeil ignorés sauf `WaveDue` ; `Died` : un monstre de vague ne réapparaît pas ; `public int WakeGroup(string name)`)
- Modifier : `RpgZoneSimulationFactory.Create` (calcule `Dormant` : `zone.Triggers.SelectMany(t => t.Actions).OfType<SpawnGroupAction>()` nomme l'id ou le `Group`)
- Créer : `Vortex.Rpg.Monsters/Zones/SpawnGroupExecutor.cs` (`ZoneActionExecutor<SpawnGroupAction>` : `(ctx.Simulation as RpgZoneSimulation)?.Spawner.WakeGroup(action.Spawn)`)
- Modifier : `MonstersModule.cs` (`services.AddZoneAction<SpawnGroupExecutor>();`)
- Tests : `Vortex.Rpg.Monsters.Tests/SpawnWaveTests.cs`, ajouts à `MonsterSpawnerTests.cs`

- [ ] **Étape 1 : tests rouges.**
  - `APoint_HoldsBetweenMinAndMax` (min 2, max 4, `FixedRandom` → 3 vivants, et après une mort la réapparition ramène à 3).
  - `ADormantPoint_BringsNothingAlone` (10 ticks avec joueur : 0 monstre).
  - `WakingIt_BringsOneWave_WithoutRespawn` (vague de 2 ; les deux tués ; 60 s plus tard toujours 0).
  - `AWakeWhileTheWaveLives_IsIgnored` ; `AWakeAfterTheWaveDied_BringsAnother`.
  - `WakingByGroup_WakesEveryPointOfIt` (deux points `group: "ambush"`).
  - `AWakeWithNoPlayer_WaitsForOne` (point d'attention 4 : `WaveDue` reste vrai sans joueur, la vague naît au premier tick avec joueur).
  - `TheExecutor_WakesTheZonesSpawner` (via une `RpgZoneSimulation` du monde de test et un `ZoneRuleContext`).
- [ ] **Étape 2 :** échec. **Étape 3 :** implémenter (tirage par `zone.Random.Next(min, max + 1)` ; `max < min` → `min`).
- [ ] **Étape 4 :** vert. **Étape 5 : commit** `monstres : effectif min..max, groupes en sommeil et vagues`.

---

### Tâche 7 : conditions et activation par proximité des points

**Fichiers :**
- Modifier : `Vortex.Rpg/Zones/IZoneRuntime.cs` (+ `bool ConditionsHold(IReadOnlyList<ZoneCondition> conditions);` sans joueur, + `IEnumerable<(int X, int Y)> PlayerCells();`) et les trois `FakeZoneRuntime` (réglables : `Conditions = true`, `Players = []`)
- Modifier : `RoomZoneRuntime.cs` (`ConditionsHold` → `room.ZoneTriggers.ConditionsHold(c, PlayerId.Invalid)` ; `PlayerCells` → positions des avatars joueurs)
- Modifier : `MonstersConfig.cs` (+ `AoiRadius` 12, `AoiSleepSeconds` 30, `SpawnConditionCheckMs` 1000 ; `Problems()` refuse < 1), `appsettings.json` de `Vortex.Main` si la section y est listée
- Modifier : `SpawnPoint.cs` (+ `bool Active`, `long NextCheckAtMs`, `long LastNearAtMs`), `MonsterSpawner.cs` (`Tick` : vérification périodique ; dormir = retirer les monstres du point hors combat, vider `Due`)
- Tests : `Vortex.Rpg.Monsters.Tests/SpawnActivationTests.cs`, `MonstersConfigTests.cs`

- [ ] **Étape 1 : tests rouges.**
  - `FalseConditions_SpawnNothing` ; `TurningFalse_RemovesIdleMonsters_KeepsFighting` (point d'attention 5) ; `TurningTrueAgain_Refills`.
  - `WithAoi_APointWakesOnlyWhenAPlayerIsNear` (joueur à 20 cases : 0 ; à 10 : effectif) ;
    `NobodyNearForTheDelay_ItSleeps` (idle retirés, en combat gardés) ; `WithoutAoi_ItWakesForAnyPlayerInTheZone`.
  - `MonstersConfigTests` : `AoiRadius 0` refusé avec sa clé.
- [ ] **Étape 2 :** échec. **Étape 3 :** implémenter. Distance : Chebyshev du joueur à la case la plus proche
  de l'aire du point. La vérification d'un point se fait au plus toutes les `SpawnConditionCheckMs`.
  « Hors combat » = `!combatant.InCombat`. Retirer = `combat` oublie le combattant puis
  `zone.RemoveMonster` (réutiliser le chemin de `Dismiss`).
- [ ] **Étape 4 :** vert, et `RpgZoneSimulationTests` / `SummonTests` toujours verts (leurs fakes ont un joueur proche et des conditions vraies par défaut).
- [ ] **Étape 5 : commit** `monstres : conditions et activation par proximité des points d'apparition`.

---

### Tâche 8 : niveau conseillé

**Fichiers :**
- Modifier : `Vortex.Rooms/Grains/Zones/Modules/ZoneAreaModule.cs` (après la bulle d'aire) et `RoomAvatarModule.Combat.cs` (après la lecture des faits : niveau de la zone)
- Créer : `Vortex.Rooms/Grains/Zones/Modules/ZoneLevelAdvice.cs` (`static void Check(RoomGrain room, ZoneRuntimeState zone, PlayerId p, string key, int recommended)`)
- Test : `Vortex.Rooms.Tests/Zones/ZoneLevelAdviceTests.cs`

> **Révisée après la relecture des tâches 1–2 :** le noyau ne lit pas le niveau. Il pose au registre la
> condition du document `PlayerLevelCondition(N, 0)` ; s'il n'y a pas de réponse déclarée
> (`ZoneRuleRegistry.Answers`), il ne dit rien. Le message ne cite donc pas le niveau du joueur.

- [ ] **Étape 1 : tests rouges.** `UnderTheLevel_ThePlayerIsWarned` (bulle `zone.level` « Niveau conseillé : 5. », `PlayerLevelEvaluator` déclaré) ; `AtTheLevel_Nothing` ; `OncePerAreaAndVisit` (sortir et rentrer dans l'aire : une seule ; quitter la zone et revenir : une nouvelle) ; `WithoutFacts_Nothing` ; `NobodyAnswersLevel_Nothing` ; `TheZonesOwnLevel_OnArrival`.
- [ ] **Étape 2 :** échec. **Étape 3 :** implémenter. Clé « déjà dit » `level:<aire>` / `level:zone`
  dans un `HashSet<string>` du `ZoneVisitor` (oublié avec lui au départ — « par visite »).
- [ ] **Étape 4 :** vert. **Étape 5 : commit** `zones : avertissement de niveau conseillé`.

---

### Tâche 9 : validateur

**Fichiers :**
- Modifier : `Vortex.Primitives/Rpg/Content/IRpgContentService.cs` (+ `IReadOnlyList<string> GetItemIds();` non retirés), `Vortex.Rpg/Content/Import/RpgContentService.cs`, `Vortex.WebApi.Tests/FakeRpgContentService.cs`
- Modifier : `Vortex.World/ZoneValidator.cs` (ctor : `IEnumerable<IZoneRuleDeclaration>? rules = null`), `Vortex.World/ZoneRuntimeRules.cs`
- Tests : `Vortex.Database.Tests/World/ZoneRuntimeRulesTests.cs`, `ZoneValidatorTests.cs`

- [ ] **Étape 1 : tests rouges.**
  - `AConditionNobodyDeclared_IsInert` / `ADeclaredOne_IsNot` (déclarations passées au validateur).
  - `QuestStateTrigger_StaysInert` ; `MonsterKilledTrigger_IsNotInert`.
  - `PlayerLevel_MinAboveMax_IsAnError`, `PlayerLevel_MinBelowOne_IsAnError`.
  - `HasItem_UnknownItem_IsAnError`, `HasItem_WithoutContent_IsAWarning`.
  - `SpawnGroup_NamingNothing_IsAnError` ; `SpawnGroup_NamingAGroup_IsFine`.
  - `MonsterKilled_TargetMustBeASpawnOrItsMonster` ; `PlayerLevelTrigger_TargetMustBeALevel` ; `ItemOwned_TargetMustBeAnItem`.
  - `ADormantSpawn_IsReported` (`Info`, code `DormantSpawn`).
- [ ] **Étape 2 :** échec. **Étape 3 :** implémenter. `ZoneRuntimeRules.Check` reçoit
  `IReadOnlySet<Type> declared` et `IReadOnlySet<string>? items` ; « sans effet » pour une
  condition/action dont le type n'est pas dans `declared` ; `RpgTriggers` réduit à `QuestState`.
  Les tests existants qui attendaient « sans effet » pour niveau/objet/`SpawnGroup` passent la liste
  de déclarations du jeu livré (`ShippedRules.All` : un utilitaire de test qui liste les 8 types des
  rooms + `PlayerLevel`, `HasItem`, `SpawnGroup`).
- [ ] **Étape 4 :** vert. **Étape 5 : commit** `éditeur : le validateur suit les règles déclarées et vérifie les nouvelles`.

---

### Tâche 10 : preuve en direct, docs

**Fichiers :**
- Modifier : `.claude/skills/run-vortex-emulator/driver.mjs` (scénario `rules`) et la table du `SKILL.md`
- Modifier : `AGENTS.md` (ligne « Where code goes » : une condition ou action de zone → pièce déclarée par `AddZoneCondition<T>` / `AddZoneAction<T>` dans son module) ; `docs/architecture.md` si les règles de zone y sont décrites
- Créer : `docs/superpowers/plans/2026-10-10-zone-rules-z1.bilan.md`

- [ ] **Étape 1 :** scénario `rules` (spec §13) : publie la zone `proof_rules` par l'API de l'éditeur
  (aire restreinte `PlayerLevel {min 2}`, point `wolves` ordinaire, point `ambush` en sommeil réveillé
  par un déclencheur `MonsterKilled wolves`, déclencheurs `PlayerLevel "2"` et `ItemOwned` avec
  `ShowDialogue`) ; compte `run-skill-e` niveau 1 ; vérifie dans l'ordre : refus sur l'aire
  (2243 `zone.restricted`), mort d'un loup (attaque via le chemin `combat`), apparition des monstres
  `ambush` (996), route XP → bulle niveau 2, route admin objet → bulle objet, entrée sur l'aire.
- [ ] **Étape 2 :** `VortexCloudQualityGate` en arrière-plan, vert ; scénario `rules` sur l'instance alt, sortie gardée.
- [ ] **Étape 3 :** docs, bilan (tâches, relectures, mineures différées, chiffres de tests).
- [ ] **Étape 4 : commit** `zones : scénario rules, docs et bilan Z1`.
