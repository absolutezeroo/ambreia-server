# RPG M1a — Premier combat : plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **Mode choisi par le propriétaire :** Claude implémente chaque tâche, un relecteur passe toutes les 2 tâches, puis une relecture finale. Le propriétaire a demandé d'enchaîner directement du plan à l'exécution (« tu attaques directement », 2026-10-09).

**Goal :** des monstres (pets Habbo) apparaissent dans une zone, un clic en attaque un (approche puis frappes automatiques), les monstres agressifs attaquent à vue, la mort et l'XP fonctionnent, et les PV et la mana d'un personnage sont les mêmes partout.

**Architecture :**
- **Le noyau `Vortex.Rpg`** définit deux contrats :
  - `IZoneRuntime`, la couture que `RoomGrain` implémente : grille, entités, marche, diffusion, horloge ;
  - `IZoneSimulation` et sa fabrique, ce que la room appelle : entrée et sortie d'un joueur, choix de cible, marche manuelle, tick, rafale d'entrée.
- **`Vortex.Rpg.Combat`** porte les combattants, la chaîne de coup, la menace, l'état de combat, la régénération, les morts et l'XP.
- **`Vortex.Rpg.Monsters`** porte les apparitions, l'IA `melee` et la fabrique de simulation, qui assemble combat et monstres. `Vortex.Rooms` ne référence que le noyau ; la fabrique concrète arrive par DI.
- **Les PV et la mana** appartiennent à `PlayerCharacterGrain`, qui les persiste. La zone les lit une fois à l'entrée et les rend une fois à la sortie.

**Tech Stack :** C# .NET 10, Orleans, EF Core + Pomelo, xUnit + FluentAssertions, `RoomHarness`, `FakeProxy`.

**Spec :** `docs/superpowers/specs/2026-10-09-rpg-combat-m1a-design.md`.

## Global Constraints

- **Ids réseau (sortants) :**

  | Id | Message |
  |---|---|
  | 9012 | `RpgCombatEvents` |
  | 9013 | `RpgVitals` |
  | 9014 | `RpgSelfVitals` |
  | 9015 | `RpgDeath` |
  | 9016 | `RpgCombatResult` |

  Aucun paquet entrant nouveau : `RpgTargetSelect` (9010) sert. `RpgTarget` (9009) gagne la sorte `monster`.
- **Monstre dans `Users` :** un pet (`RoomObjectType.Pet` = 2), avec :
  - `WebId = −ObjectId` ;
  - nom = l'id de contenu ;
  - figure de pet ;
  - `PetLevel` = son niveau ;
  - propriétaire 0 / `""`.
- **Règles de combat (spec §6) :**

  | Élément | Règle |
  |---|---|
  | Chaîne de coup | esquive 100, dégâts de base 200, critique 300, armure 400, minimum 900 |
  | Armure | `dégâts × 100 / (100 + armure)` |
  | Critique | × 1,5 |
  | Cadence | `2000 × 100 / Vitesse%`, au moins 1000 ms |
  | Sortie de combat | 5 s après le dernier coup |
  | Régénération | 2 % du maximum par seconde, hors combat seulement |
  | Menace | dégâts × `ThreatMultiplier` |
  | Fantôme | 5 s |
  | Réapparition | 50 % des PV |
  | Usure à la mort | 10 % de la durabilité maximale |
  | XP | ±10 % par niveau d'écart, +50 % au plus, 0 à 8 niveaux ou plus en dessous |

- **Tout est réglable dans `Vortex:Combat`** (tableau §10 de la spec).
- **Grains :**
  - la room appelle le personnage (lecture à l'entrée, écriture à la sortie, XP, usure), mais le personnage n'appelle jamais la room ;
  - le personnage passe par la présence, sans attendre ;
  - les appels indépendants vers plusieurs grains partent en `Task.WhenAll` ;
  - aucune écriture en base par coup.
- **INV-003 :** l'étape `combat` ne fait rien tant qu'aucun joueur n'est dans la room.
- **Conventions du dépôt :**
  - un type par fichier, le namespace suit le dossier ;
  - code par Edit/Write, commits en français avec chemins explicites, sur `main` ;
  - ne jamais commiter `scripts/sql/import_reference_data.sql` ni `docs/superpowers/specs/2026-10-06-vortex-mmorpg-master-design.md` ;
  - un grain modifié → relire `.claude/rules/grains.md`.
- **Émulateur du propriétaire :** il tourne sur :40000 depuis `bin/Debug`, verrouillé. Les preuves passent par l'instance `alt` (`Vortex.Main/bin/alt`, :40010, base `vortex_schema_check`, script `amb-alt.ps1` du scratchpad). Toute migration de base se fait par script `scripts/sql/` sur la base visée, jamais pendant qu'un émulateur y tourne.

## Décisions de ce plan (écarts assumés à la spec)

1. **`Vortex.Rooms` ne référence que le noyau `Vortex.Rpg`, pas `Rpg.Combat` ni `Rpg.Monsters`.** La spec §4.1 les fait référencer. Ici, la room parle à `IZoneSimulation`, et la fabrique concrète (`Vortex.Rpg.Monsters`) arrive par DI. C'est plus découplé et ça respecte toujours la règle de dépendance.
2. **Une zone de room sans document de zone (ni `ZoneRuntime`) ne simule rien** : simulation nulle.
3. **Les monstres n'ont pas de logique d'objet (`Logic` nulle).** Les endroits qui déréférencent `avatar.Logic` sont gardés.
4. **Le chemin d'un monstre est calculé par l'A* plat** (`FindPath(start, goal, canOccupy, canMove)`), où `canOccupy` exclut les cases `Safe`. Pour un monstre, la validation de pas du tick d'avatars n'utilise pas les règles de joueur : une case `Safe` ou bloquée arrête sa marche.

## Review Focus

1. **Un joueur qui part (déconnexion, changement de zone) en plein combat ou en fantôme.** Ses PV sont rendus une fois au personnage (en fantôme : les PV de réapparition, 50 %), il sort des tables de menace, et aucun coup ne le vise après. Test : Tâche 5, `APlayerWhoLeaves_IsWrittenBackOnce_AndForgotten`.
2. **Deux joueurs tuent le même monstre au même tick, ou un joueur part juste avant la mort.** L'XP va une fois à chaque participant encore présent, jamais deux fois, et pas à celui parti. Test : Tâche 5, `AMonsterKilled_GivesXpOnceToEachParticipantStillHere`.
3. **Un monstre poursuivi qui passe la laisse pendant qu'on le frappe.** Il rentre invulnérable, les coups qui lui sont destinés ne partent pas, et il revient plein. Test : Tâche 6, `AMonsterPastItsLeash_GoesHomeInvulnerable_AndHeals`.
4. **Une room réactivée.** Pas de monstre en double, et pas de naissance tant qu'aucun joueur n'est là. Test : Tâche 6, `NoPlayer_NoSpawn` et `ASecondActivation_SpawnsAfresh_WithoutDoubles`.
5. **Un changement de stats en zone** (objet équipé, niveau gagné) : les PV maximum suivent et les PV courants restent bornés. Test : Tâche 8, `AStatsChangeInZone_UpdatesTheCombatant_AndClampsHealth`.

---

### Task 1 : Projets `Vortex.Rpg.Combat` et `Vortex.Rpg.Monsters`, sorte de contenu `monster`

**Files :**
- Create : `Vortex.Rpg.Combat/Vortex.Rpg.Combat.csproj`, `Vortex.Rpg.Combat.Tests/Vortex.Rpg.Combat.Tests.csproj`, `Vortex.Rpg.Monsters/Vortex.Rpg.Monsters.csproj`, `Vortex.Rpg.Monsters.Tests/Vortex.Rpg.Monsters.Tests.csproj` (copier les références et propriétés de `Vortex.Rpg.Characters` et de `Vortex.Rpg.Tests`) ; les ajouter à `Vortex.Cloud.sln` (`dotnet sln add`) ; `Vortex.Main` les référence.
- Create : `Vortex.Rpg/Content/Definitions/MonsterDefinition.cs`, `MonsterStatBlock.cs`, `Vortex.Rpg/Content/Kinds/MonsterKind.cs`
- Modify : `Vortex.Rpg/RpgModule.cs` (`AddSingleton<IContentKind, MonsterKind>()`), `scripts/hooks/check-architecture-walls.mjs` (`PLATFORM` += `'Rpg.Combat', 'Rpg.Monsters'`), `Vortex.Hosting.Tests/Rpg/ContentSchemaTests.cs` (sorte `monster`), `Vortex.World/ZoneValidator.cs` (la référence `monster` est résolue contre le contenu, si le validateur a accès au contenu ; sinon elle reste « différée » et une ligne de registre le dit)
- Create : `schemas/content/monster.schema.json` (généré), `content/monster/forest_wolf.json`, `content/monster/proof_brute.json`, lignes dans `content/texts/fr.txt`
- Test : `Vortex.Rpg.Tests/Content/MonsterKindTests.cs`

**Interfaces :**
- Produces :

```csharp
// Vortex.Rpg.Content.Definitions
public sealed record MonsterStatBlock
{
    public int Health { get; init; }
    public int Armor { get; init; }
    public int DamageMin { get; init; }
    public int DamageMax { get; init; }
    public double CritPercent { get; init; }
    public double DodgePercent { get; init; }
}

public sealed record MonsterDefinition : ContentDefinition
{
    /// <summary>Habbo pet figure "<type> <race> <colour>".</summary>
    public required string Figure { get; init; }
    public string Behavior { get; init; } = "melee";
    public bool Aggressive { get; init; }
    public required MonsterStatBlock Base { get; init; }
    public MonsterStatBlock PerLevel { get; init; } = new();
    public int AttackRange { get; init; } = 1;
    public int AttackIntervalMs { get; init; } = 2000;
    /// <summary>0: twice the spawn's aggro radius.</summary>
    public int LeashRadius { get; init; }
    public int Xp { get; init; }
    public int XpPerLevel { get; init; }
}
```

  `MonsterKind : ContentKind<MonsterDefinition>` (`Kind => "monster"`) refuse :
  - `NegativeValue` : une valeur négative ;
  - `DamageRange` : `DamageMin > DamageMax` (au niveau 1, et `PerLevel.DamageMin > PerLevel.DamageMax` aussi) ;
  - `BadRange` : `AttackRange < 1` ;
  - `BadInterval` : `AttackIntervalMs < 500` ;
  - `BadPercent` : un pourcentage hors de [0 ; 100] ;
  - `BadFigure` : une figure qui n'a pas trois éléments séparés par des espaces, dont deux entiers ;
  - `NoHealth` : `Base.Health < 1`.

  La clé de comportement n'est pas vérifiée ici (le noyau ne connaît pas les comportements) : `Vortex.Rpg.Monsters` la vérifie au démarrage de la simulation (Tâche 6).

- [ ] **Step 1 : tests qui échouent.** `MonsterKindTests` suit le modèle des tests de `ItemKind` (`KindRulesTests`) :
  - `AValidWolf_HasNoFinding` ;
  - un test par code d'erreur ci-dessus (Theory sur des records modifiés par `with`).
- [ ] **Step 2 :** `dotnet test Vortex.Rpg.Tests --filter "FullyQualifiedName~MonsterKindTests"` → FAIL (compilation).
- [ ] **Step 3 : implémenter.**
  - Les records et `MonsterKind`, avec les messages en anglais, comme `ItemKind`.
  - Les projets (`dotnet new classlib` puis aligner sur `Vortex.Rpg.Characters.csproj` : TargetFramework, analyzers, `InternalsVisibleTo` du projet de tests).
  - Les murs.
  - La sorte de contenu : ligne dans `RpgModule`, ajout à `ContentSchemaTests`, puis `UPDATE_CONTENT_SCHEMAS=1 dotnet test Vortex.Hosting.Tests -o Vortex.Hosting.Tests/bin/alt --filter ContentSchemaTests`.
  - **Contenu :**
    - `monster.forest.wolf` : figure `"0 3 C0A060"`, agressif, base 40 PV, armure 2, dégâts 3–5, critique 5, esquive 3 ; par niveau 12 PV, armure +1, dégâts +1/+2 ; portée 1, cadence 2000, XP 20 + 6 par niveau.
    - `monster.proof.brute` : figure `"4 0 FFFFFF"`, agressif, base 5000 PV, armure 50, dégâts 400–500, cadence 1000, XP 0.
    - Textes : `monster.forest.wolf.name=Loup des bois` et `monster.proof.brute.name=Brute d'essai`.
- [ ] **Step 4 :**
  - `dotnet test Vortex.Rpg.Tests` → PASS ;
  - `dotnet test Vortex.Hosting.Tests -o Vortex.Hosting.Tests/bin/alt --filter "FullyQualifiedName~Content"` → PASS ;
  - `node scripts/hooks/check-architecture-walls.mjs` → OK ;
  - `dotnet build Vortex.Cloud.sln` (vers un fichier) → 0 erreur, compte tenu du verrou : les projets de test se construisent dans leur propre `bin`, et `Vortex.Main` vers `bin/alt`.
- [ ] **Step 5 : commit** `combat : projets Rpg.Combat et Rpg.Monsters, contenu monster (loup, brute d'essai)`.

---

### Task 2 : PV et mana du personnage (persistés), usure

**Files :**
- Modify : `Vortex.Database/Entities/Rpg/PlayerCharacterEntity.cs` (`current_health int?`, `current_mana int?`, `vitals_at datetime?`) ; regénérer `InitialCreate` (`scripts/ef.ps1`, skill `changing-the-schema`) ; Create `scripts/sql/2026-10-09-character-vitals.sql` (`ALTER TABLE … ADD COLUMN …`, re-jouable avec un test d'existence de la colonne)
- Create : `Vortex.Primitives/Rpg/Characters/CharacterCombatProfile.cs`
- Modify : `Vortex.Primitives/Rpg/Characters/Grains/IPlayerCharacterGrain.cs` ; Create `Vortex.Rpg.Characters/Grains/PlayerCharacterGrain.Vitals.cs` ; Modify le chargement et l'écriture différée du grain (`PlayerCharacterGrain.cs`), et `CharacterInventory` (méthode d'usure)
- Modify : `Vortex.Rpg.Characters/Configuration/CharacterConfig.cs` (`RegenPercentPerSecond` = 2, lu aussi par le combat via `Vortex:Combat` ; ici, le taux hors zone)
- Test : `Vortex.Database.Tests/Rpg/CharacterVitalsTests.cs` (le grain se teste là, comme les autres tests du personnage), `Vortex.Rpg.Tests/Items/CharacterInventoryWearTests.cs`

**Interfaces :**
- Produces :

```csharp
// Vortex.Primitives.Rpg.Characters
[GenerateSerializer, Immutable]
public sealed record CharacterCombatProfile
{
    [Id(0)] public required CharacterStatsSnapshot Stats { get; init; }
    [Id(1)] public required int Health { get; init; }
    [Id(2)] public required int Mana { get; init; }
    [Id(3)] public required int Level { get; init; }
    /// <summary>Attack range in tiles, from the class.</summary>
    [Id(4)] public required int Range { get; init; }
    [Id(5)] public required int ThreatMultiplier { get; init; }
}

// IPlayerCharacterGrain
/// <summary>Stats, current health and mana (out-of-zone regeneration applied, bounded to the
/// maximum), level, range and threat multiplier; null without a character (spec M1a §7).</summary>
public Task<CharacterCombatProfile?> GetCombatProfileAsync();
/// <summary>The zone gives health and mana back when the player leaves it (one write, spec M1a §4.4).</summary>
public Task SetVitalsAsync(int health, int mana);
/// <summary>Equipped items lose <paramref name="percent"/> % of their max durability (death, spec M1a §8.1).</summary>
public Task WearEquipmentAsync(int percent);

// Vortex.Rpg.Items.CharacterInventory
/// <summary>Each equipped item with a durability loses percent % of its max (at least 1), down to 0;
/// answers whether any reached 0 (its stats go).</summary>
public bool WearEquipped(int percent, Func<string, int> maxDurabilityOf);
```

- [ ] **Step 1 : tests qui échouent.**
  - `CharacterVitalsTests` (SQLite ou InMemory selon `.claude/rules/tests.md`, sur le modèle des tests de personnage de `Vortex.Database.Tests/Rpg`) :
    - `ANewCharacter_IsFull` ;
    - `SetVitals_IsKept_AcrossAReactivation` (écriture différée puis relecture) ;
    - `OutOfZone_HealthRegenerates_FromVitalsAt` (horloge injectée : 10 s → +20 % du maximum, borné) ;
    - `Health_IsBoundedToTheMaximum` ;
    - `WithoutACharacter_TheProfileIsNull`.
  - `CharacterInventoryWearTests` :
    - `Wear_TakesAPercentOfTheMax_DownToZero` ;
    - `AnItemWithoutDurability_IsNotWorn` ;
    - `ReachingZero_IsReported`.
- [ ] **Step 2 :** lancer → FAIL.
- [ ] **Step 3 : implémenter.**
  - **Colonnes et migration** (skill `changing-the-schema`).
  - **Le grain :**
    - il garde `_health`, `_mana`, `_vitalsAt`, lus au chargement (null = plein) ;
    - `GetCombatProfileAsync` applique la régénération `(now − vitalsAt) × 2 %/s` (bornée), puis renvoie le profil ; `Range` et `ThreatMultiplier` viennent de la classe (« Sans classe » : portée 1) ;
    - `SetVitalsAsync` borne, puis met `_vitalsAt = now` et `_dirty = true` ;
    - l'écriture différée existante écrit aussi ces trois colonnes ;
    - `WearEquipmentAsync` s'appuie sur `WearEquipped` (via `Touch`), puis `EquipmentChangedAsync()` si un objet est tombé à 0.
  - **L'horloge** est un `TimeProvider` en propriété, défaut `TimeProvider.System` (modèle de `ZoneTriggerModule.Time`).
- [ ] **Step 4 :** `dotnet test Vortex.Database.Tests --filter "FullyQualifiedName~CharacterVitals"` et `dotnet test Vortex.Rpg.Tests` → PASS.
- [ ] **Step 5 :** appliquer le script SQL à `vortex_schema_check`, avec `MYSQL_PWD` et une instance `alt` arrêtée. Commit `combat : PV et mana du personnage persistés, usure de l'équipement`.
- [ ] **Step 6 : relecture des tâches 1–2** (`grain-rules-reviewer`, le grain du personnage est touché). Les remarques Critiques ou Importantes sont corrigées avant la tâche 3.

---

### Task 3 : Le fil — cinq messages, `RpgTarget` monstre, `Users` d'un pet

**Files :**
- Create (`Vortex.Protocol/Messages/Outgoing/Rpg/Combat/`) : `RpgCombatEventsMessageComposer.cs`, `RpgCombatHit.cs`, `RpgVitalsMessageComposer.cs`, `RpgCombatantVitals.cs`, `RpgSelfVitalsMessageComposer.cs`, `RpgDeathMessageComposer.cs`, `RpgCombatResultMessageComposer.cs`
- Create (`Vortex.Revisions/Revision20260701/Serializers/Rpg/Combat/`) : un serializer par composer ; `Maps/RpgCombatMap.cs` ; Modify `Headers.cs` (9012–9016 sortants ; retirer le commentaire de réservation), `Revision20260701.cs`
- Modify : `Vortex.Protocol/Messages/Outgoing/Rpg/Interaction/RpgTargetMessageComposer.cs` (`public const string KindMonster = "monster";`)
- Test : `Vortex.Revisions.Tests/Rpg/RpgCombatWireTests.cs`, un ajout à `Vortex.Revisions.Tests/Room/RoomAvatarWireTests.cs` (un pet de monstre : `WebId` négatif, pas de propriétaire)

**Interfaces :**
- Produces (tous `[GenerateSerializer, Immutable]`, ids `[Id(n)]` dans l'ordre des champs) :

```csharp
public sealed record RpgCombatHit
{
    public required int AttackerId { get; init; }
    public required int TargetId { get; init; }
    public required int Amount { get; init; }
    public const string Hit = "hit", Crit = "crit", Miss = "miss";
    public required string Outcome { get; init; }
    public required int Health { get; init; }
    public required int MaxHealth { get; init; }
    public required bool Killed { get; init; }
}
public sealed record RpgCombatEventsMessageComposer : IComposer { public required ImmutableArray<RpgCombatHit> Hits { get; init; } }
public sealed record RpgCombatantVitals { public required int ObjectId { get; init; } public required int Health { get; init; } public required int MaxHealth { get; init; } }
public sealed record RpgVitalsMessageComposer : IComposer { public required ImmutableArray<RpgCombatantVitals> Combatants { get; init; } }
public sealed record RpgSelfVitalsMessageComposer : IComposer
{ public required int Health { get; init; } public required int MaxHealth { get; init; } public required int Mana { get; init; } public required int MaxMana { get; init; } public required bool InCombat { get; init; } }
public sealed record RpgDeathMessageComposer : IComposer { public required int RespawnSeconds { get; init; } }
public sealed record RpgCombatResultMessageComposer : IComposer
{ public const string SafeZone = "safe_zone", Unreachable = "unreachable", Dead = "dead"; public required string Code { get; init; } }
```

  Fil : un entier `count`, puis chaque élément dans l'ordre des champs (`WriteInteger`, `WriteString`, `WriteBoolean`).

- [ ] **Step 1 : tests qui échouent.** `RpgCombatWireTests` copie `Serialize` de `RpgInteractionWireTests`, avec un test par message, champs dans l'ordre client et `Remaining == 0` :
  - deux coups pour `RpgCombatEvents` ;
  - deux combattants pour `RpgVitals`.

  Dans `RoomAvatarWireTests`, `AMonsterPet_IsAPetWithoutOwner`.
- [ ] **Step 2 :** FAIL (compilation).
- [ ] **Step 3 :** implémenter (modèle `RpgChatMap`, serializers comme `RpgChatMessageComposerSerializer`).
- [ ] **Step 4 :**
  - `dotnet test Vortex.Revisions.Tests` → PASS ;
  - `node scripts/hooks/check-inert-declarations.mjs` : les cinq composers sont signalés tant que rien ne les émet. C'est prévu, et la Tâche 7 les fait émettre ; noter la sortie.
- [ ] **Step 5 : commit** `combat : messages RpgCombatEvents, RpgVitals, RpgSelfVitals, RpgDeath, RpgCombatResult`.

---

### Task 4 : Le runtime de zone, la simulation, le cœur du combat

**Files :**
- Create (`Vortex.Rpg/Zones/`) : `IZoneRuntime.cs`, `IZoneSimulation.cs`, `IZoneSimulationFactory.cs`, `ZoneCell.cs` (si `Vortex.Primitives.World.Layers.ZoneCell` ne convient pas, sinon le réutiliser), `MonsterAppearance.cs`, `NoZoneSimulation.cs` (la simulation qui ne fait rien)
- Create (`Vortex.Rpg.Combat/`) : `Configuration/CombatConfig.cs` ; `Combatants/Combatant.cs`, `CombatantKind.cs`, `ThreatTable.cs` ; `Hits/IHitStep.cs`, `Hits/HitContext.cs`, `Hits/ICombatantStats.cs` (dans `Vortex.Rpg.Combat` : M1b, compagnons et monstres en dépendent déjà) ; `Hits/DodgeStep.cs`, `BaseDamageStep.cs`, `CritStep.cs`, `ArmorStep.cs`, `MinimumStep.cs`, `HitResolver.cs` ; `CombatMath.cs` (Chebyshev, XP) ; `CombatModule.cs` (`IHostPluginModule`)
- Test : `Vortex.Rpg.Combat.Tests/HitResolverTests.cs`, `CombatMathTests.cs`, `ThreatTableTests.cs`, `Support/FakeZoneRuntime.cs`, `Support/FixedRandom.cs`

**Interfaces :**
- Produces (noyau, `Vortex.Rpg.Zones`) :

```csharp
/// <summary>What the zone (V1: RoomGrain) gives combat and monsters (spec M1a §4.2). Synchronous: it is
/// called inside the room's turn; only sending is asynchronous.</summary>
public interface IZoneRuntime
{
    long NowMs { get; }
    Random Random { get; }
    int Width { get; }
    int Height { get; }
    bool IsWalkable(int x, int y);
    bool HasLineOfSight(int fromX, int fromY, int toX, int toY);
    ZoneCellRules RulesAt(int x, int y);
    (int X, int Y)? PositionOf(int objectId);
    bool IsWalking(int objectId);
    /// <summary>Puts a monster avatar (a Habbo pet) on a free cell; its room object id.</summary>
    int SpawnMonster(MonsterAppearance appearance, int x, int y);
    void RemoveMonster(int objectId);
    /// <summary>Walks an avatar towards a cell; false when the path budget of this tick is spent or no path exists.</summary>
    bool WalkTo(int objectId, int x, int y);
    void StopWalking(int objectId);
    void Teleport(int objectId, int x, int y);
    /// <summary>"lay" while a ghost, null to stand.</summary>
    void SetPosture(int objectId, string? posture);
    /// <summary>The zone's respawn entry, else its default arrival.</summary>
    (int X, int Y)? RespawnCell();
    Task BroadcastAsync(IComposer composer);
    Task SendToPlayerAsync(PlayerId player, IComposer composer);
}

public sealed record MonsterAppearance(string DefinitionId, string Figure, int Level);

/// <summary>What a zone runs for its combat (spec M1a §4.3); created per room activation.</summary>
public interface IZoneSimulation
{
    Task PlayerEnteredAsync(PlayerId player, int objectId, CancellationToken ct);
    Task PlayerLeftAsync(PlayerId player, CancellationToken ct);
    /// <summary>True when <paramref name="objectId"/> is a monster: the simulation answered the
    /// player (RpgTarget, attack started or RpgCombatResult); false lets the room treat it.</summary>
    Task<bool> SelectTargetAsync(PlayerId player, int objectId, CancellationToken ct);
    /// <summary>The player walked on his own: his attack stops.</summary>
    void PlayerWalked(PlayerId player);
    Task TickAsync(CancellationToken ct);
    /// <summary>For the entry burst: every combatant's health.</summary>
    IComposer EntryVitals();
    /// <summary>The player's stats changed (equipment, level): max health follows, health bounded.</summary>
    void PlayerStatsChanged(PlayerId player, CharacterStatsSnapshot stats);
}

public interface IZoneSimulationFactory
{
    IZoneSimulation Create(IZoneRuntime runtime, ZoneDocument zone);
}

// Vortex.Rpg.Combat
public interface IHitStep
{
    int Priority { get; }
    /// <summary>Changes the hit; returns false to stop the chain (a miss).</summary>
    bool Apply(HitContext hit);
}
public sealed class HitContext
{
    public required ICombatantStats Attacker { get; init; }
    public required ICombatantStats Target { get; init; }
    public required Random Random { get; init; }
    public double Damage { get; set; }
    public string Outcome { get; set; } = "hit";
}
public interface ICombatantStats
{
    bool IsPlayer { get; }
    int Power { get; }        // player: primary stat
    int DamageMin { get; }    // monster
    int DamageMax { get; }
    int Armor { get; }
    double CritPercent { get; }
    double DodgePercent { get; }
}
```

  Dans `Vortex.Rpg.Combat` :
  - **`Combatant : ICombatantStats`** porte :

    | Champ | Rôle |
    |---|---|
    | `ObjectId`, `Kind` | identité |
    | `PlayerId?` | null pour un monstre |
    | `Level`, `Health`, `MaxHealth`, `Mana`, `MaxMana` | état |
    | `Range`, `AttackIntervalMs`, `ThreatMultiplier` | attaque |
    | `TargetId?`, `NextSwingAtMs`, `LastHostileAtMs`, `InCombat` | combat en cours |
    | `GhostUntilMs?`, `Invulnerable` | mort, retour d'un monstre |
    | `Threat` (`ThreatTable`) | menace |
    | `Participants` (`HashSet<PlayerId>`) | joueurs qui ont touché le monstre |

  - **`HitResolver(IEnumerable<IHitStep>)`** applique les étapes triées par priorité (ordre stable) et renvoie `(int amount, string outcome)`.
  - **`CombatMath`** :
    - `Chebyshev(ax, ay, bx, by)` ;
    - `XpFor(int monsterXp, int monsterLevel, int playerLevel, CombatConfig)` ;
    - `AttackIntervalMs(int speedPercent, CombatConfig)`.
  - **`ThreatTable`** : `Add(int objectId, long amount)`, `Remove(int)`, `Clear()`, `int? Top`, `bool Contains(int)`, `IsEmpty`.

- [ ] **Step 1 : tests qui échouent.**
  - `HitResolverTests`, avec un `FixedRandom` qui rend des valeurs choisies :
    - `ADodge_IsAMiss_WithNoDamage` ;
    - `APlayerHit_IsPowerTimesTheRoll` ;
    - `AMonsterHit_IsBetweenItsMinAndMax` ;
    - `ACrit_IsOneAndAHalf` ;
    - `Armor_Reduces_AsTheSpecSays` (armure 100 → moitié) ;
    - `AHit_IsAtLeastOne` ;
    - `ADeclaredStep_RunsAtItsPriority` (une étape de test à 350 qui double les dégâts, entre critique et armure).
  - `CombatMathTests` :
    - `Chebyshev_IsTheLargerAxis` ;
    - `Xp_SameLevel_IsFull` ;
    - `Xp_PlusTenPercentPerLevelAbove_UpToFifty` ;
    - `Xp_MinusTenPercentPerLevelBelow` ;
    - `Xp_EightLevelsBelow_IsZero` ;
    - `AttackInterval_FollowsSpeed_WithAFloor`.
  - `ThreatTableTests` :
    - `TheTop_IsTheMostThreat` ;
    - `Removing_MovesTheTop` ;
    - `Ties_KeepTheFirstComer`.
- [ ] **Step 2 :** `dotnet test Vortex.Rpg.Combat.Tests` → FAIL (compilation).
- [ ] **Step 3 :** implémenter.
  - Les étapes livrées :
    - **Esquive (100) :** `Random.NextDouble() × 100 < Target.DodgePercent` → `Outcome = miss`, `Damage = 0`, `false`.
    - **Dégâts de base (200) :**
      - joueur : `Power × (min + Random.NextDouble() × (max − min))` ;
      - monstre : `Random.Next(DamageMin, DamageMax + 1)`.
    - **Critique (300) :** `NextDouble() × 100 < Attacker.CritPercent` → × `CritMultiplier`, `crit`.
    - **Armure (400) :** `Damage × 100 / (100 + Target.Armor)`.
    - **Minimum (900) :** `Math.Max(1, round)`.
  - `CombatConfig` reprend le tableau §10 de la spec, section `Vortex:Combat`.
  - **`CombatModule`** déclare :
    - les cinq `IHitStep` ;
    - `Configure<CombatConfig>`.
  - **`NoZoneSimulation`** : tout ne fait rien, `SelectTargetAsync` → `false`, `EntryVitals` → `RpgVitals` vide.
- [ ] **Step 4 :** `dotnet test Vortex.Rpg.Combat.Tests` → PASS.
- [ ] **Step 5 : commit** `combat : runtime de zone, simulation, chaîne de coup, menace, calculs`.
- [ ] **Step 6 : relecture des tâches 3–4** (`feature-dev:code-reviewer`).

---

### Task 5 : La zone de combat — attaque, sortie de combat, régénération, morts, XP, messages groupés

**Files :**
- Create (`Vortex.Rpg.Combat/Zones/`) : `CombatZone.cs` (+ parties `CombatZone.Players.cs`, `CombatZone.Swings.cs`, `CombatZone.Deaths.cs`, `CombatZone.Outbox.cs`), `CombatOutbox.cs`
- Create (`Vortex.Primitives/Events/Combat/`) : `MonsterKilledEvent.cs`, `PlayerDiedEvent.cs`, `CombatEnteredEvent.cs`, `CombatLeftEvent.cs`
- Test : `Vortex.Rpg.Combat.Tests/CombatZoneTests.cs` (sur `FakeZoneRuntime`, avec un faux `IGrainFactory` pour les personnages)

**Interfaces :**
- Consumes : Tâche 2 (`GetCombatProfileAsync`, `SetVitalsAsync`, `WearEquipmentAsync`, `GainXpAsync`), Tâche 3 (composers), Tâche 4 (runtime, `Combatant`, `HitResolver`, `CombatMath`, `CombatConfig`).
- Produces :

```csharp
public sealed partial class CombatZone(IZoneRuntime runtime, HitResolver hits, CombatConfig config,
    IGrainFactory grains, IEventPublisher events, ILogger logger)
{
    public IReadOnlyDictionary<int, Combatant> Combatants { get; }
    public bool HasPlayers { get; }
    public Task PlayerEnteredAsync(PlayerId player, int objectId, CancellationToken ct);  // reads the profile, sends RpgSelfVitals
    public Task PlayerLeftAsync(PlayerId player, CancellationToken ct);                  // SetVitalsAsync once, forgotten everywhere
    public Combatant AddMonster(int objectId, int level, MonsterDefinition def);
    public void RemoveMonster(int objectId);
    /// <summary>Starts the player's attack on a monster; a refusal code or null.</summary>
    public string? StartAttack(PlayerId player, int monsterId);
    public void StopAttack(PlayerId player);
    /// <summary>A monster aims at a player (its AI decides who).</summary>
    public void SetMonsterTarget(int monsterId, int? playerObjectId);
    public void PlayerStatsChanged(PlayerId player, CharacterStatsSnapshot stats);
    /// <summary>Swings due, combat exits, regeneration, ghosts back; then the tick's messages.</summary>
    public Task TickAsync(CancellationToken ct);
    /// <summary>Monsters that died this tick, for the spawner.</summary>
    public IReadOnlyList<int> DiedMonsters { get; }
    public IComposer EntryVitals();
}
```

  **Règles du tick, dans l'ordre :**
  1. **Les coups :** chaque combattant vivant, non fantôme, avec une cible vivante, dont `NextSwingAtMs ≤ now`.
     - **La cible est à portée** (Chebyshev ≤ `Range`) et en ligne de vue :
       - le coup passe par `HitResolver` (pas de coup vers un monstre `Invulnerable`, ni vers ou depuis une case `Safe`) ;
       - les PV de la cible baissent, la menace monte si la cible est un monstre, et l'attaquant joueur entre dans `Participants` ;
       - les deux passent `InCombat`, avec `LastHostileAtMs = now` ;
       - le coup va dans la boîte d'envoi ;
       - `NextSwingAtMs = now + AttackIntervalMs`.
     - **Hors de portée, pour un joueur :** `runtime.WalkTo(cible)` s'il ne marche pas déjà vers elle. Une marche impossible (`false` deux ticks de suite, sans budget épuisé) → `RpgCombatResult(unreachable)` et l'attaque s'arrête.
  2. **Les morts :**
     - **Un joueur à 0 PV** passe fantôme : `GhostUntilMs = now + GhostSeconds × 1000`, posture `lay`, arrêt des marches, retrait de toutes les tables de menace. `RpgDeath` part à lui seul, `PlayerDiedEvent` sur le bus, et l'usure en tâche détachée journalisée (`WearEquipmentAsync(DeathWearPercent)`).
     - **Un monstre à 0 PV** est retiré (`runtime.RemoveMonster`) et entre dans `DiedMonsters`. L'XP va à chaque participant **encore dans la zone** (`CombatMath.XpFor`, `GainXpAsync`, source `monster:<id>`), appels en `Task.WhenAll`, échecs journalisés. `MonsterKilledEvent` est publié.
  3. **Les fantômes au bout de leur délai** sont téléportés à `RespawnCell()`, avec PV = `RespawnHealthPercent` % du max et posture null.
  4. **Les sorties de combat :** `InCombat` et `now − LastHostileAtMs ≥ CombatTimeoutMs`, sans aucun monstre qui l'ait dans sa table → sortie et `CombatLeftEvent`. L'entrée publie `CombatEnteredEvent`.
  5. **La régénération,** chaque seconde pleine : hors combat et non fantôme, + `RegenPercentPerSecond` % du max (PV et mana), borné. Les combattants changés vont dans `RpgVitals`.
  6. **Les envois groupés :**
     - un `RpgCombatEvents` s'il y a eu des coups ;
     - un `RpgVitals` s'il y a des changements hors coups (zone) ;
     - un `RpgSelfVitals` par joueur dont PV, mana ou `InCombat` ont changé.

  **Sortie d'un joueur :**
  - `SetVitalsAsync(PV, mana)` une fois ; un fantôme part avec les PV de réapparition ;
  - il quitte toutes les tables et `Participants` ;
  - les monstres qui le visaient n'ont plus de cible.

- [ ] **Step 1 : tests qui échouent** (`CombatZoneTests`, `FakeZoneRuntime` à horloge manuelle et `FixedRandom` ; un faux personnage via `FakeProxy<IGrainFactory>` qui enregistre `SetVitals`, `GainXp` et `Wear`) :
  - `Entering_ReadsTheProfile_AndSendsSelfVitals` ;
  - `APlayerInRange_HitsAtHisCadence` ;
  - `APlayerOutOfRange_WalksToTheTarget` ;
  - `AnUnreachableTarget_StopsTheAttack_WithUnreachable` ;
  - `AnAttackFromOrOntoASafeCell_IsRefused` (`safe_zone`) ;
  - `AHitOnAMonster_AddsThreatTimesTheMultiplier` ;
  - `AMonsterKilled_GivesXpOnceToEachParticipantStillHere` (Review Focus 2) ;
  - `APlayerKilled_BecomesAGhost_ThenRespawnsAtHalfHealth_AndIsWorn` ;
  - `NoHitForFiveSeconds_LeavesCombat_AndRegenerates` ;
  - `InCombat_NothingRegenerates` ;
  - `APlayerWhoLeaves_IsWrittenBackOnce_AndForgotten` (Review Focus 1, en combat et en fantôme) ;
  - `OneTick_SendsOneCombatEventsMessage` (deux coups le même tick → un message) ;
  - `NoPlayer_TheTickDoesNothing`.
- [ ] **Step 2 :** FAIL.
- [ ] **Step 3 :** implémenter.
- [ ] **Step 4 :** `dotnet test Vortex.Rpg.Combat.Tests` → PASS.
- [ ] **Step 5 : commit** `combat : la zone de combat — attaque, morts, XP, régénération, messages groupés`.

---

### Task 6 : Les monstres — apparitions, IA `melee`, laisse, réapparition, la simulation

**Files :**
- Create (`Vortex.Rpg.Monsters/`) : `Behaviors/IMonsterBehavior.cs`, `Behaviors/MeleeBehavior.cs`, `Behaviors/MonsterBrain.cs` (état et contexte passés au comportement), `Behaviors/MonsterState.cs` ; `Spawns/MonsterSpawner.cs`, `Spawns/SpawnPoint.cs`, `Spawns/LiveMonster.cs` ; `Zones/RpgZoneSimulation.cs`, `Zones/RpgZoneSimulationFactory.cs` ; `MonstersModule.cs`
- Test : `Vortex.Rpg.Monsters.Tests/MonsterSpawnerTests.cs`, `MeleeBehaviorTests.cs`, `RpgZoneSimulationTests.cs`, `Support/` (le `FakeZoneRuntime` est copié ou partagé via un projet de test support ; choisir la copie si le partage oblige un nouveau projet)

**Interfaces :**
- Consumes : Tâche 1 (`MonsterDefinition`), Tâche 4 (`IZoneRuntime`, `IZoneSimulation`, `IZoneSimulationFactory`), Tâche 5 (`CombatZone`).
- Produces :

```csharp
public interface IMonsterBehavior
{
    string Key { get; }
    /// <summary>One decision per combat tick: state, target, movement.</summary>
    void Think(MonsterBrain brain);
}
public enum MonsterState { Idle, Chase, Fight, Return }
public sealed class MonsterBrain   // what a behavior reads and changes
{
    public required LiveMonster Monster { get; init; }
    public required CombatZone Combat { get; init; }
    public required IZoneRuntime Runtime { get; init; }
    public required int AggroRadius { get; init; }
    public required int LeashRadius { get; init; }
}
public sealed class LiveMonster
{
    public required int ObjectId { get; init; }
    public required MonsterDefinition Definition { get; init; }
    public required SpawnPoint Spawn { get; init; }
    public required (int X, int Y) Home { get; init; }
    public MonsterState State { get; set; }
    public long NextWanderAtMs { get; set; }
}
```

  `RpgZoneSimulationFactory : IZoneSimulationFactory` crée un `CombatZone` et un `MonsterSpawner` (pour les `ZoneSpawn` du document), et renvoie `RpgZoneSimulation`.
  - **Le tick :** naissances dues (seulement s'il y a des joueurs), `Think` de chaque monstre vivant, `CombatZone.TickAsync`, puis réapparitions programmées pour `DiedMonsters`.
  - **`SelectTargetAsync`, sur un monstre de la zone :** `RpgTarget` (sorte `monster`, nom = id, niveau, classe vide) au joueur, puis `StartAttack`. Un refus envoie `RpgCombatResult`. Rend `true`.

  **Règles :**
  - **Naissances :** au premier tick avec un joueur, chaque `ZoneSpawn` fait naître `Max` monstres :
    - case tirée dans `Area`, marchable, non `Safe`, libre ;
    - niveau tiré dans [`LevelMin` ; `LevelMax`] ;
    - stats au niveau L = `Base + PerLevel × (L − 1)` ;
    - `CombatZone.AddMonster`.

    Un mort renaît `RespawnSeconds` plus tard, au plus une naissance par point et par tick. Un monstre inconnu, retiré, ou de comportement non déclaré → avertissement une fois par point, rien ne naît.
  - **Le comportement `melee` :**
    - **Repos.** Il erre : toutes les 4 à 8 s, une case marchable non `Safe` à au plus `WanderRadius` de `Home`. Agressif (ou case `Hostile`), il voit un joueur vivant, non fantôme, hors `Safe`, à au plus `AggroRadius` et en ligne de vue → menace 1 sur lui, puis Poursuite.
    - **Poursuite et Combat.**
      - Cible = `Threat.Top` ; s'il n'en a pas → Retour.
      - Plus loin que `LeashRadius` de `Home` → Retour.
      - Cible à portée → `SetMonsterTarget` et arrêt de la marche (Combat).
      - Sinon, `WalkTo` vers la cible, seulement si la cible a bougé depuis la dernière demande ou s'il ne marche pas.
      - Une cible sur une case `Safe` est retirée de la table.
    - **Retour.**
      - Au départ : `Invulnerable = true`, table vidée, cible nulle, marche vers `Home`.
      - Arrivé à 1 case ou moins : PV au max, `RpgVitals`, `Invulnerable = false`, Repos.
  - **Un monstre touché alors qu'il est au repos** passe en Poursuite, pas en riposte immédiate. C'est la menace ajoutée par le coup qui le déclenche.

- [ ] **Step 1 : tests qui échouent.**
  - `MonsterSpawnerTests` :
    - `NoPlayer_NoSpawn` ;
    - `AFirstPlayer_SpawnsMaxPerPoint_InTheArea_AtALevelInRange` ;
    - `ADeadMonster_RespawnsAfterItsDelay` ;
    - `AnUnknownMonster_SpawnsNothing_AndWarnsOnce` ;
    - `ASecondActivation_SpawnsAfresh_WithoutDoubles` (deux simulations successives sur le même runtime de test : la seconde repart de zéro).
  - `MeleeBehaviorTests` :
    - `AnAggressiveMonster_ChasesAPlayerInItsAggroRadius` ;
    - `APassiveMonster_IgnoresUntilHit` ;
    - `APlayerOnASafeCell_IsNeverChased` ;
    - `AMonsterPastItsLeash_GoesHomeInvulnerable_AndHeals` (Review Focus 3) ;
    - `AtRange_TheMonsterAttacks` ;
    - `ADeclaredBehavior_IsUsedByItsKey` (un comportement de test, clé `statue`, qui ne fait rien).
  - `RpgZoneSimulationTests` :
    - `SelectingAMonster_AnswersRpgTarget_AndStartsTheAttack` ;
    - `SelectingSomethingElse_IsLeftToTheRoom`.
- [ ] **Step 2 :** FAIL.
- [ ] **Step 3 :** implémenter. `MonstersModule` déclare :
  - `IMonsterBehavior` → `MeleeBehavior` ;
  - `IZoneSimulationFactory` → `RpgZoneSimulationFactory`.

  Les deux modules (`CombatModule`, `MonstersModule`) sont ajoutés à `Vortex.Main/Program.cs` à côté de `CharactersModule`.
- [ ] **Step 4 :** `dotnet test Vortex.Rpg.Monsters.Tests` → PASS.
- [ ] **Step 5 : commit** `combat : monstres — apparitions, IA melee, laisse, réapparition, simulation de zone`.
- [ ] **Step 6 : relecture des tâches 5–6** (`feature-dev:code-reviewer`).

---

### Task 7 : La room — runtime de zone, avatars de monstre, étape de tick, entrée et sortie, cible

**Files :**
- Modify : `Vortex.Rooms/Vortex.Rooms.csproj` (référence `Vortex.Rpg`)
- Create : `Vortex.Rooms/Grains/Zones/Combat/RoomZoneRuntime.cs` (l'adaptateur `IZoneRuntime` sur `RoomGrain`), `Vortex.Rooms/Object/Avatars/Monster/RoomMonsterAvatar.cs`
- Modify :
  - `RoomGrain.cs` : le constructeur prend `IZoneSimulationFactory`, le champ `Simulation`, l'étape `"combat"` dans `RunRoomTickAsync` avec sa borne `NextCombatBoundaryMs` dans `RoomLiveState`, et la création de la simulation dans `ApplyZoneAsync` (une nouvelle par application de zone, `NoZoneSimulation` sans zone) ;
  - `RoomGrain.Avatar.cs` : `PlayerEnteredAsync` après la création de l'avatar, `PlayerLeftAsync` avant son retrait ;
  - `RoomGrain.Entry.cs` : `Simulation.EntryVitals()` dans la rafale d'entrée d'une zone ;
  - `RoomTargetModule.cs` : `Simulation.SelectTargetAsync` d'abord ; `0` ou toute autre cible appelle `StopAttack` via `PlayerWalked`, ou un `SelectTargetAsync(0)` géré dans la simulation ;
  - `RoomAvatarModule.Walk.cs` : une marche par `ActionContext` d'un joueur appelle `Simulation.PlayerWalked` ;
  - la validation de pas de `RoomAvatarTickSystem`, pour un monstre : case `Safe` ou bloquée → arrêt ;
  - les déréférencements de `avatar.Logic` sont gardés (`RoomAvatarTickSystem.cs` ~L197/L214, `RoomMapModule.Avatar.cs` ~L34/L52, `RoomObjectModule.cs` ~L223) ;
  - `RoomAvatarModule.cs` : `NextObjectId()` exposé pour les monstres.
- Modify : `Vortex.Rooms.Tests/Support/RoomHarness.cs` (passe une `IZoneSimulationFactory`, par défaut celle qui rend `NoZoneSimulation`) ; Create `Vortex.Rooms.Tests/Zones/RoomZoneRuntimeTests.cs`, `Vortex.Rooms.Tests/Zones/RoomCombatTests.cs`
- Modify : `Vortex.Hosting.Tests/Architecture/RoomEntryPayloadTests.cs` (si la rafale d'entrée y est épinglée : `RpgVitalsMessageComposer` dans une zone)

**Interfaces :**
- Consumes : Tâches 4–6.
- Produces : `RoomZoneRuntime(RoomGrain room) : IZoneRuntime` :
  - `NowMs` → `room.NowMs()` ;
  - `Random` → `Random.Shared`, remplaçable par les tests ;
  - **ligne de vue** par rayon de Bresenham sur les cases : une case non marchable, fermée ou désactivée sur le trajet (bornes exclues) la coupe ;
  - `RulesAt` → `ZoneRuntime.RulesAt` ;
  - **`SpawnMonster`** :
    - crée un `RoomMonsterAvatar` (type `Pet`, `WebId = −objectId`, nom = id de contenu, figure, `PetLevel`) sur la case, l'enregistre dans `AvatarsByObjectId` et la carte, et diffuse `Users` (chemin d'`AttatchObjectAsync` sans logique) ;
    - c'est un appel synchrone dans le tour, qui renvoie l'id ; la diffusion part sans être attendue mais reste ordonnée dans le tick : elle est mise dans une file que l'étape `combat` vide à la fin du tick.
  - **`WalkTo`** :
    - joueur : `AvatarModule.WalkAvatarToAsync` sans contexte joueur, pour ne pas déclencher `PlayerWalked` ;
    - monstre : `PathingSystem.FindPath(start, goal, canOccupy: marchable && !Safe, canMove)`, sous le budget `MaxPathSearchesPerTick` remis à zéro à chaque tick ;
  - **`RespawnCell`** : l'entrée de sorte `respawn`, sinon `Compiled.DefaultArrival`.

- [ ] **Step 1 : tests qui échouent.**
  - `RoomZoneRuntimeTests` (`RoomHarness.CreateZoneAsync`) :
    - `LineOfSight_IsCutByAWall` ;
    - `ASpawnedMonster_IsAPetInUsers` ;
    - `AMonsterPath_AvoidsSafeCells` ;
    - `ThePathBudget_IsPerTick` ;
    - `RespawnCell_IsTheRespawnEntry_ElseTheArrival`.
  - `RoomCombatTests`, une zone de harness avec un `ZoneSpawn` de loup et la vraie fabrique (avec un faux personnage) :
    - `AZoneWithSpawns_SpawnsWolvesWhenAPlayerEnters` ;
    - `ClickingAWolf_StartsTheAttack` ;
    - `WalkingByHand_StopsTheAttack` ;
    - `LeavingTheRoom_GivesVitalsBack` ;
    - `TheEntryBurst_CarriesVitals`.
- [ ] **Step 2 :** FAIL.
- [ ] **Step 3 :** implémenter.
  - `Vortex.Main` enregistre déjà les modules (Tâche 6).
  - **Le constructeur de `RoomGrain` change**, et la gotcha de `AGENTS.md` s'applique : tous les `CreateWithIntegerKey<RoomGrain>` passent la fabrique (`grep`).
- [ ] **Step 4 :**
  - `dotnet test Vortex.Rooms.Tests` → PASS (tout le projet) ;
  - `dotnet test Vortex.Hosting.Tests -o Vortex.Hosting.Tests/bin/alt` → PASS ;
  - `node scripts/hooks/check-inert-declarations.mjs` → OK ;
  - `check-architecture-walls` → OK (le mur 9 interdit un `DbContext` dans `Grains/Zones`).
- [ ] **Step 5 : commit** `combat : la room exécute la simulation — monstres, attaque au clic, PV à l'entrée et à la sortie`.

---

### Task 8 : Les stats qui changent en zone, les métriques

**Files :**
- Modify : `Vortex.Primitives/Players/Grains/IPlayerPresenceGrain.Avatar.cs` (`OnCharacterStatsChangedAsync(CharacterStatsSnapshot stats)`), `Vortex.Players/Grains/PlayerPresenceGrain.Avatar.cs` (vers la room active, sans attente : `LogAndForget`)
- Create : `Vortex.Primitives/Rooms/Grains/IRoomCombat.cs` (`UpdateCombatantStatsAsync(PlayerId player, CharacterStatsSnapshot stats)`), dans l'agrégat `IRoomGrain`, `GetRoomCombat` ; `Vortex.Rooms/Grains/RoomGrain.Combat.cs` (façade → `Simulation.PlayerStatsChanged`)
- Modify : `Vortex.Rpg.Characters/Grains/PlayerCharacterGrain*.cs` (après chaque changement de stats : équipement, niveau, classe, contenu → présence, sans attente)
- Modify : `IVortexMetrics` + `VortexMetrics` (`CombatHit(string outcome)`, `MonsterKilled()`, `PlayerDied()`, `MonsterPathSearch(bool refused)`) ; les appels depuis `CombatZone` et `RoomZoneRuntime`
- Test : `Vortex.Rooms.Tests/Zones/RoomCombatTests.cs` (ajout), `Vortex.Players.Tests/Presence/PresenceStatsTests.cs`

- [ ] **Step 1 : tests qui échouent.**
  - `AStatsChangeInZone_UpdatesTheCombatant_AndClampsHealth` (Review Focus 5) ;
  - `ThePresence_ForwardsStatsToItsRoom_WithoutWaiting` ;
  - `WithoutARoom_NothingIsForwarded`.
- [ ] **Step 2 :** FAIL.
- [ ] **Step 3 :** implémenter.
  - L'interleaving ne change pas : le personnage n'attend pas la présence, et la présence n'attend pas la room (`LogAndForget`).
  - `check-authorization-surface` : `UpdateCombatantStatsAsync` ne prend pas d'`ActionContext` (appel serveur) ; s'il est listé, `--update` après revue.
- [ ] **Step 4 :** `dotnet test Vortex.Rooms.Tests`, `Vortex.Players.Tests`, `Vortex.Database.Tests --filter Character` → PASS.
- [ ] **Step 5 : commit** `combat : stats changées en zone, métriques`.
- [ ] **Step 6 : relecture des tâches 7–8** (`grain-rules-reviewer` : room, présence et personnage).

---

### Task 9 : La preuve `combat`, la documentation, la porte

**Files :**
- Modify : `.claude/skills/run-vortex-emulator/driver.mjs` (en-têtes 9012–9016 sortants ; commande `combat`), `SKILL.md`
- Modify : `docs/architecture.md` (« Flow 11 — combat »), `AGENTS.md` (ligne « A monster behaviour or a hit step »), la spec M1a (statut « livrée »)

- [ ] **Step 1 : la commande `cmdCombat`.**
  - **Préparation.** Elle pousse le contenu (`content`, qui porte les monstres), puis publie une zone de preuve `proof_combat`, sur le modèle de `cmdZone` / `zoneDocument`. Cette zone a :
    - une entrée `arrival` et une entrée `respawn` ;
    - une aire `Safe` de 3 × 3 autour de l'arrivée ;
    - un `ZoneSpawn` de 2 `monster.forest.wolf` niveau 1 à 6 cases ;
    - un `ZoneSpawn` de `monster.proof.brute` dans un coin éloigné.
  - **Les étapes :**
    1. A entre ; `Users` contient des pets (type 2) aux noms `monster.forest.wolf` ; `RpgSelfVitals` arrive.
    2. Depuis la case `Safe`, A clique un loup → `RpgCombatResult(safe_zone)`.
    3. A sort de la case sûre, puis clique un loup → `RpgTarget` (`monster`), des `RpgCombatEvents` (coups de A), le loup meurt (`killed`), et `RpgCharacterProgress` montre l'XP gagnée.
    4. L'autre loup, agressif, engage A : un coup du loup sur A dans `RpgCombatEvents`.
    5. A marche vers la brute, qui le tue → `RpgDeath(5)`, puis réapparition. `RpgSelfVitals` donne la moitié des PV max, à ±1 près.
    6. A quitte la zone et entre dans une autre : `RpgSelfVitals` donne les mêmes PV.
- [ ] **Step 2 : preuve.**
  - Construire `Vortex.Main` vers `bin/alt`, appliquer le script SQL de la Tâche 2 à `vortex_schema_check` (instance arrêtée), puis `amb-alt.ps1 start`, `amb-alt.ps1 combat` → OK, puis `smoke`, `travel`, `target` et `chat` → OK (non-régression).
  - Copier la sortie dans le registre.
- [ ] **Step 3 : documentation.**
  - **Flow 11** : runtime de zone, simulation, tick, monstres, coups, morts, PV portés par le personnage.
  - **`AGENTS.md`** : une ligne « A monster behaviour or a hit step → an `IMonsterBehavior` (key) / `IHitStep` (priority) declared by DI in its module ».
  - **`SKILL.md`** : la commande `combat`.
  - **La spec** : statut « livrée ».
- [ ] **Step 4 : porte de qualité.** Étape par étape (`gate-alt.sh` du scratchpad, avec la liste des projets de tests complétée par `Rpg.Combat` et `Rpg.Monsters`) → tout vert.
- [ ] **Step 5 : commit** `combat : preuve driver combat, documentation`.
- [ ] **Step 6 : relecture finale** (`general-purpose`, opus), correctifs avec test RED→GREEN, puis le contrat réseau §9 envoyé à la session client.
