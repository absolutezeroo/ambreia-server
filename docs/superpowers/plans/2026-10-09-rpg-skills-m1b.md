# RPG M1b — Compétences et effets de statut : plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **Mode choisi par le propriétaire :** Claude implémente chaque tâche, un relecteur passe toutes les 2 tâches, puis une relecture finale (opus). Spec approuvée le 2026-10-09 (« Ces partit »).

**Goal :** un joueur d'une vraie classe remplit sa barre de 6 et lance des compétences (instantanées, incantées, canalisées, sur soi, un allié, un ennemi, le sol ou une zone) qui infligent des dégâts, soignent et posent des effets de statut ; les compétences s'apprennent seules en montant de niveau.

**Architecture :**
- **Le cœur `Vortex.Rpg`** gagne les sortes de contenu `skill` et `status`, le calcul des compétences connues et trois méthodes de `IZoneSimulation` (lancer, annuler, « le joueur a marché »).
- **`Vortex.Rpg.Combat`** gagne des points d'extension et ne nomme ni compétence ni statut : systèmes de tick (`ICombatSystem`), parties de combattant, stats de base et effectives (`IStatModifierSource`), options de coup, opérations partagées (dégâts, soin, menace, envoi).
- **Le nouveau projet `Vortex.Rpg.Skills`** porte les lancers (`CastSystem`), les statuts (`StatusSystem`), les types d'effet (`ISkillEffect`, par clé) et deux étapes de coup ; tout est déclaré par DI dans `SkillsModule`.
- **Le personnage** garde la barre (colonne `action_bar`) et, en mémoire, les statuts persistants. **La room** apprend la marche ralentie et l'immobilisation, et route les trois nouveaux paquets.

**Tech Stack :** C# .NET 10, Orleans, EF Core + Pomelo, xUnit + FluentAssertions, `FakeProxy`, `RoomHarness`, `FakeZoneRuntime`.

**Spec :** `docs/superpowers/specs/2026-10-09-rpg-skills-m1b-design.md` (lire aussi M1a : `2026-10-09-rpg-combat-m1a-design.md`).

## Global Constraints

- **Ids réseau :** entrants 9013 `RpgSkillCast`, 9014 `RpgActionBarSet`, 9015 `RpgCastCancel` ; sortants 9018 `RpgSkills`, 9019 `RpgSkillResult`, 9020 `RpgCastStart`, 9021 `RpgCastEnd`, 9022 `RpgCooldowns`, 9023 `RpgStatuses`, 9024 `RpgSkillEvents`. Ordre des champs : spec §10, à la lettre. `RpgCombatEvents` (9012) ne change pas.
- **Codes de refus** (`RpgSkillResult`) : `dead`, `stunned`, `casting`, `unknown_skill`, `not_learned`, `invalid_target`, `unreachable`, `no_line_of_sight`, `safe_zone`, `cooldown`, `global_cooldown`, `no_mana`, plus pour la barre `bad_slot`.
- **Raisons de fin** (`RpgCastEnd`) : `done`, `cancelled`, `interrupted`, `failed`.
- **Ordre des contrôles** (spec §6.1) : lanceur (`dead`, `stunned`, `casting`) → compétence (`unknown_skill`, `not_learned`) → cible (`invalid_target`) → case sûre (`safe_zone`) → portée et vue (approche) → `cooldown` → `global_cooldown` → `no_mana`.
- **`Vortex:Skills`** : `GlobalCooldownMs` 1000, `HealThreatPercent` 50, `MaxStatusesPerCombatant` 32. **`Vortex:Characters:ActionBarSize`** 6 (Décision 5). Aucune autre constante de capacité.
- **Tick** (spec §4.3) : systèmes (statuts ordre 100, incantations ordre 200) → coups automatiques → morts → sorties de combat → régénération → réapparitions → envois.
- **Étapes de coup** : esquive 100, dégâts de base 200, critique 300, **modificateurs de dégâts 350**, armure 400, minimum 900, **bouclier 950**.
- **Énumérations JSON en snake_case** (`ContentJson`) : `around_caster`, `apply_status`, `crit_percent`, `damage_dealt_percent`…
- **Grains :** la room n'attend le personnage que pour ce que M1a attend déjà ; le personnage ne l'appelle jamais ; le grain du personnage seul écrit `action_bar`. Interleaving : aucune méthode nouvelle n'est `[AlwaysInterleave]`.
- **Couches :** `Vortex.Rooms` ne référence que le cœur `Vortex.Rpg` ; `Vortex.Rpg.Skills` → `Vortex.Rpg.Combat` → `Vortex.Rpg` ; `Vortex.Rpg.Monsters` ne référence pas `Skills`.
- **Disposition :** un type par fichier ; le namespace suit le dossier ; un dossier = une sorte de chose.
- **Commits** en français, chemins explicites, terminés par les deux lignes `Co-Authored-By` / `Claude-Session` ; jamais `scripts/sql/import_reference_data.sql` ni `docs/superpowers/specs/2026-10-06-vortex-mmorpg-master-design.md`.
- **Portes :** `Vortex.Main/bin/Debug` peut être verrouillé par l'émulateur du propriétaire : construire Main et Hosting.Tests en `-o …/bin/alt` (script de porte pas à pas) ; la porte complète au premier plan.

## Décisions de ce plan (écarts assumés à la spec)

1. **Modificateurs `strength`/`intelligence`/`agility`** : remplacés par **`power`** (la zone ne connaît pas la stat principale ; `power` est ce qu'elles changeraient). Liste livrée : `power`, `armor`, `crit_percent`, `dodge_percent`, `speed_percent`, `damage_dealt_percent`, `damage_taken_percent`. Aucune compétence de départ n'en est touchée.
2. **Limite par intention sur 9013** (spec §10.1) : le limiteur existant ne couvre que l'écran des personnages (`IAccountPhaseMessage`). En jeu, le seau global (50/s) s'applique et la zone refuse un lancer pendant le délai global ou une incantation sans rien calculer (Review Focus 4). Pas de nouveau limiteur.
3. **Validation des effets à l'import** : `SkillKind` (cœur) reçoit par DI les règles `ISkillEffectRule` que les types d'effet de `Vortex.Rpg.Skills` implémentent ; le cœur ne nomme aucun effet.
4. **Puissance d'un monstre** = moyenne de ses dégâts min et max (spec §5.2), posée à `AddMonster`.
5. **Taille de la barre** (spec §11 la met dans `Vortex:Skills`) : elle va dans **`Vortex:Characters:ActionBarSize`** (`CharacterConfig`), car c'est le personnage qui la garde et `Vortex.Rpg.Characters` ne référence pas `Vortex.Rpg.Skills`.

## Review Focus

1. **Un joueur qui part (déconnexion, changement de zone) pendant une incantation ou une canalisation** : elle s'arrête (`RpgCastEnd(cancelled)`), aucun effet ne tombe après, une incantation ne coûte rien ; ses statuts non persistants tombent, les persistants sont rendus. Test : Tâche 7, `LeavingMidCast_CancelsWithoutCost_AndLandsNothing`.
2. **La cible meurt pendant l'incantation (tuée par un autre)** : `RpgCastEnd(cancelled)`, pas de mana prise, pas d'exception ; une zone ignore les morts. Test : Tâche 7, `TheTargetDyingMidCast_CancelsTheCast`.
3. **Étourdi en pleine marche ou en pleine incantation** : marche arrêtée, incantation `interrupted`, plus de coups ; à la fin de l'étourdissement, tout reprend (un monstre repart en poursuite). Test : Tâche 5, `AStun_StopsSwingsAndWalk_ThenEverythingResumes` ; Tâche 7, `AStunMidCast_Interrupts`.
4. **Paquets répétés** (9013 en rafale, pendant le délai global, pendant une incantation) : refus avec code, jamais deux effets ni deux coûts. Test : Tâche 7, `ACastSpammedDuringItsGlobalCooldown_ResolvesOnce`.
5. **Bouclier, dégâts subis en % et critique ensemble** : l'ordre de la chaîne donne l'absorption juste ; le bouclier disparaît pile à épuisement ; l'attaque de base est absorbée aussi. Test : Tâche 5, `AShield_AbsorbsAfterModifiersAndCrit_ThenBreaks`.

---

### Task 1 : Projet `Vortex.Rpg.Skills`, sortes de contenu `skill` et `status`, les 12 compétences

**Files :**
- Create : `Vortex.Rpg.Skills/Vortex.Rpg.Skills.csproj`, `Vortex.Rpg.Skills/SkillsModule.cs`, `Vortex.Rpg.Skills/Configuration/SkillsConfig.cs`, `Vortex.Rpg.Skills/Effects/{DamageEffect,HealEffect,ApplyStatusEffect,InterruptEffect,DispelEffect,ThreatEffect}.cs` (règles seulement dans cette tâche) ; `Vortex.Rpg.Skills.Tests/Vortex.Rpg.Skills.Tests.csproj`
- Create (cœur) : `Vortex.Rpg/Content/Definitions/{SkillDefinition,SkillEffectDefinition,SkillType,SkillTarget,StatusDefinition,StatusType,StatusStacking,StatModifierDefinition,ModifierStat}.cs`, `Vortex.Rpg/Content/Kinds/{SkillKind,StatusKind}.cs`, `Vortex.Rpg/Content/Rules/ISkillEffectRule.cs`, `Vortex.Rpg/Skills/KnownSkills.cs`
- Create (contenu) : `content/skill/*.json` (12), `content/status/*.json` (7 : `weakened`, `stunned`, `focused`, `burning`, `frozen`, `renewed`, `aegis`), textes dans `content/texts/fr.txt`, `schemas/content/{skill,status}.schema.json`
- Modify : `Vortex.Rpg/RpgModule.cs`, `Vortex.Cloud.sln`, `Vortex.Main/Vortex.Main.csproj`, `Vortex.Main/Program.cs`, `scripts/hooks/check-architecture-walls.mjs` (`'Rpg.Skills'` dans `PLATFORM`), `Vortex.Hosting.Tests/Rpg/{ContentSchemaTests,ContentPackInRepoTests}.cs`
- Test : `Vortex.Rpg.Tests/Content/SkillKindTests.cs`, `StatusKindTests.cs`, `Vortex.Rpg.Tests/Skills/KnownSkillsTests.cs`

**Interfaces :**
- Produces :
  - `public sealed record SkillDefinition : ContentDefinition` — `List<string> Classes` (requis), `List<string> Specializations = []`, `int Level` (requis), `int Cost`, `int CooldownMs`, `bool GlobalCooldown = true`, `SkillType Type` (requis), `int CastMs`, `int ChannelMs`, `int PulseMs`, `int Range` (requis), `SkillTarget Target` (requis), `int Radius`, `int MaxTargets = 10`, `bool MoveAllowed`, `bool Unavoidable`, `List<SkillEffectDefinition> Effects` (requis), `string Animation = ""`, `string Vfx = ""`, `string Sound = ""`.
  - `public sealed record SkillEffectDefinition` — `string Kind` (requis), `double Base`, `double Coefficient`, `string? Status`, `int Amount`, `int ThreatPercent`.
  - `enum SkillType { Instant = 1, Cast = 2, Channel = 3 }`, `enum SkillTarget { Self = 1, Ally = 2, Enemy = 3, Ground = 4, AroundCaster = 5, AroundTarget = 6 }`.
  - `public sealed record StatusDefinition : ContentDefinition` — `StatusType Type`, `int DurationMs`, `int PeriodMs`, `StatusStacking Stacking = Refresh`, `int MaxStacks`, `bool Dispellable = true`, `bool Persistent`, `List<StatModifierDefinition> Modifiers = []`, `double Base`, `double Coefficient`, `int SlowPercent`.
  - `enum StatusType { Buff = 1, Debuff = 2, Dot = 3, Hot = 4, Stun = 5, Slow = 6, Shield = 7 }`, `enum StatusStacking { Replace = 1, Refresh = 2, Stack = 3, Ignore = 4 }`, `enum ModifierStat { Power = 1, Armor = 2, CritPercent = 3, DodgePercent = 4, SpeedPercent = 5, DamageDealtPercent = 6, DamageTakenPercent = 7 }`, `record StatModifierDefinition { ModifierStat Stat; double Percent; double Flat; }`.
  - `public interface ISkillEffectRule { string Kind { get; } IEnumerable<string> Problems(SkillEffectDefinition effect); }` (`Vortex.Rpg.Content.Rules`).
  - `public static class KnownSkills { public static IReadOnlyList<SkillDefinition> For(IRpgContent content, string classId, string specializationId, int level); }` — non retirées, `Classes` contient la classe, `Specializations` vide ou contient la spécialisation, `Level ≤ level` ; triées par `Level` puis `Id`.
  - `SkillsModule : IHostPluginModule` (`Key => "vortex-rpg-skills"`) enregistre `SkillsConfig` (`Vortex:Skills`) et chaque effet comme `ISkillEffectRule` (Tâche 6 ajoutera `ISkillEffect`).

- [ ] **Step 1 : tests qui échouent.** `SkillKindTests` (un `SkillKind` construit avec les 6 règles réelles de `Vortex.Rpg.Skills.Effects`) : un document valide passe ; refus `UnknownEffect` (kind `"push"`), `BadEffect` (`damage` sans `base` ni `coefficient`, `apply_status` sans `status`, `dispel` avec `amount` 0, `threat` sans `threatPercent`), `BrokenReference` (classe inconnue, statut inconnu), `CastWithoutTime` (`cast` sans `castMs`), `ChannelWithoutPulse` (`pulseMs` absent ou > `channelMs`), `AreaWithoutRadius` (`ground`/`around_*` avec `radius` 0), `NoEffect`, `NegativeValue`. `StatusKindTests` : `DotWithoutPeriod`, `StackWithoutMax`, `SlowWithoutPercent` (et hors 1–90), `NegativeValue`, durée ≤ 0. `KnownSkillsTests` : classe, spécialisation, niveau, retirée, ordre.

```csharp
[Fact]
public void ADamageEffect_WithoutBaseNorCoefficient_IsRefused()
{
    SkillDefinition skill = Firebolt() with { Effects = [new SkillEffectDefinition { Kind = "damage" }] };

    Check(skill).Should().ContainSingle(f => f.Code == "BadEffect" && f.Id == skill.Id);
}
```

- [ ] **Step 2 :** `dotnet test Vortex.Rpg.Tests --filter "FullyQualifiedName~SkillKind|FullyQualifiedName~StatusKind|FullyQualifiedName~KnownSkills"` → échec de compilation (types absents).
- [ ] **Step 3 : implémenter.** Projet `Vortex.Rpg.Skills` (copie du csproj de `Vortex.Rpg.Combat`, `InternalsVisibleTo` `Vortex.Rpg.Skills.Tests`, référence `Vortex.Rpg.Combat`, `Vortex.Rpg`, `Vortex.Primitives`, `Vortex.Protocol`, `Vortex.Logging`) et son projet de tests (copie de `Vortex.Rpg.Combat.Tests.csproj`), ajoutés à la solution (2 entrées `Project` + 12 lignes de configuration chacun, comme Combat). `SkillKind(IEnumerable<ISkillEffectRule> rules)` :

```csharp
foreach (SkillEffectDefinition e in s.Effects)
{
    if (!_rules.TryGetValue(e.Kind, out ISkillEffectRule? rule))
    {
        yield return Error("UnknownEffect", s.Id, $"Effect '{e.Kind}' is not declared by any module.");
        continue;
    }

    foreach (string problem in rule.Problems(e))
    {
        yield return Error("BadEffect", s.Id, $"Effect '{e.Kind}': {problem}");
    }

    if (e.Status is { } status && !check.Is<StatusDefinition>(status))
    {
        yield return Error("BrokenReference", s.Id, $"Status '{status}' does not exist.");
    }
}
```

`RpgModule` : `services.AddSingleton<IContentKind, SkillKind>(); services.AddSingleton<IContentKind, StatusKind>();`. Effets (règles) : `DamageEffect` et `HealEffect` exigent `base > 0 || coefficient > 0` ; `ApplyStatusEffect` exige `status` ; `DispelEffect` exige `amount ≥ 1` ; `ThreatEffect` exige `threatPercent ≥ 1` ; `InterruptEffect` rien. `Program.cs` : `AddHostPlugin<SkillsModule>()` ; `Vortex.Main.csproj` référence le projet.
- [ ] **Step 4 : contenu.** Les 12 compétences et 7 statuts de spec §16, ids exacts de la spec, `"$schema": "../../schemas/content/skill.schema.json"`. Exemple :

```json
{
  "$schema": "../../schemas/content/skill.schema.json",
  "id": "skill.mage.flame_burst",
  "kind": "skill",
  "classes": ["class.mage"],
  "level": 3,
  "cost": 18,
  "cooldownMs": 10000,
  "type": "cast",
  "castMs": 1500,
  "range": 5,
  "target": "around_target",
  "radius": 1,
  "effects": [
    { "kind": "damage", "coefficient": 0.8 },
    { "kind": "apply_status", "status": "status.burning" }
  ],
  "vfx": "fire_burst"
}
```

Statuts : `status.weakened` (debuff, 8000 ms, replace, `damage_dealt_percent` −20 %), `status.stunned` (stun, 2000, ignore), `status.focused` (buff, 10000, refresh, `crit_percent` +20 plat), `status.burning` (dot, 6000, période 2000, stack max 3, coefficient 0.2), `status.frozen` (slow 60, 5000, replace), `status.renewed` (hot, 12000, période 3000, refresh, coefficient 0.4), `status.aegis` (shield, 10000, ignore, base 20, coefficient 2). `heroic_strike` porte `{ "kind": "threat", "threatPercent": 50 }` ; `shield_bash` porte `damage` (0.5), `apply_status` `status.stunned`, `interrupt` ; `focus` a `"globalCooldown": false`. Textes : `skill.<…>.name`, `.desc`, `status.<…>.name` dans `fr.txt`. Schémas : `UPDATE_CONTENT_SCHEMAS=1 dotnet test Vortex.Hosting.Tests -o Vortex.Hosting.Tests/bin/alt --filter ContentSchemaTests` après avoir ajouté `"skill", "status"` à `Kinds()` et à `ByName` (`new SkillKind(SkillRules.All)`, `new StatusKind()`) ; `ContentPackInRepoTests` reçoit les deux sortes (règles : `new DamageEffect(), new HealEffect(), …`).
- [ ] **Step 5 :** les filtres du Step 2 + `dotnet test Vortex.Hosting.Tests -o Vortex.Hosting.Tests/bin/alt --filter "FullyQualifiedName~Content"` → verts ; `node scripts/hooks/check-architecture-walls.mjs` et `check-code-layout.mjs` → 0.
- [ ] **Step 6 : commit** `compétences : projet Vortex.Rpg.Skills, contenu skill et status, 12 compétences de départ`.

---

### Task 2 : Le fil — 3 messages entrants, 7 sortants

**Files :**
- Create : `Vortex.Protocol/Messages/Incoming/Rpg/Skills/{RpgSkillCastMessage,RpgActionBarSetMessage,RpgCastCancelMessage}.cs` ; `Vortex.Protocol/Messages/Outgoing/Rpg/Skills/{RpgSkillsMessageComposer,RpgSkillResultMessageComposer,RpgCastStartMessageComposer,RpgCastStart,RpgCastEndMessageComposer,RpgCastEnd,RpgCooldownsMessageComposer,RpgCooldown,RpgStatusesMessageComposer,RpgCombatantStatuses,RpgStatusEntry,RpgSkillEventsMessageComposer,RpgSkillImpact,RpgSkillOutcome}.cs` ; parsers `Vortex.Revisions/Revision20260701/Parsers/Rpg/Skills/*`, serializers `Serializers/Rpg/Skills/*`, `Maps/RpgSkillsMap.cs`
- Modify : `Headers.cs` (9013–9015 entrants, 9018–9024 sortants, avant `#endregion`), `Revision20260701.cs` (`new RpgSkillsMap()`)
- Test : `Vortex.Revisions.Tests/Rpg/RpgSkillsWireTests.cs`

**Interfaces :**
- Produces (records `[GenerateSerializer, Immutable]`, `[Id(n)]` dans l'ordre du fil) :
  - `RpgSkillCastMessage { string SkillId; int TargetObjectId; int X; int Y; }`, `RpgActionBarSetMessage { int Slot; string ActionId; }`, `RpgCastCancelMessage { }`.
  - `RpgSkillsMessageComposer { ImmutableArray<string> Known; ImmutableArray<string> Bar; }` (case vide = `""`).
  - `RpgSkillResultMessageComposer { string SkillId; string Code; }` + constantes des codes (Global Constraints) : `Dead`, `Stunned`, `Casting`, `UnknownSkill`, `NotLearned`, `InvalidTarget`, `Unreachable`, `NoLineOfSight`, `SafeZone`, `Cooldown`, `GlobalCooldown`, `NoMana`, `BadSlot`.
  - `RpgCastStart { int CasterObjectId; string SkillId; int DurationMs; bool Channel; int TargetObjectId; int X; int Y; }`, `RpgCastStartMessageComposer { ImmutableArray<RpgCastStart> Casts; }`.
  - `RpgCastEnd { int CasterObjectId; string SkillId; string Reason; }` + constantes `Done`, `Cancelled`, `Interrupted`, `Failed` ; `RpgCastEndMessageComposer { ImmutableArray<RpgCastEnd> Ends; }`.
  - `RpgCooldown { string SkillId; int RemainingMs; }`, `RpgCooldownsMessageComposer { int GlobalRemainingMs; ImmutableArray<RpgCooldown> Skills; }`.
  - `RpgStatusEntry { string StatusId; int RemainingMs; int Stacks; }`, `RpgCombatantStatuses { int ObjectId; ImmutableArray<RpgStatusEntry> Statuses; }`, `RpgStatusesMessageComposer { ImmutableArray<RpgCombatantStatuses> Combatants; }`.
  - `RpgSkillImpact { int CasterObjectId; string SkillId; int TargetObjectId; int X; int Y; }`, `RpgSkillOutcome { int SourceObjectId; string SourceId; int TargetObjectId; int Amount; int Absorbed; string Outcome; int Health; int MaxHealth; bool Killed; }` + constantes `Hit`, `Crit`, `Dodge`, `Heal` ; `RpgSkillEventsMessageComposer { ImmutableArray<RpgSkillImpact> Impacts; ImmutableArray<RpgSkillOutcome> Outcomes; }`.

- [ ] **Step 1 : tests qui échouent** (`RpgSkillsWireTests`, modèle `RpgCombatWireTests`) : un test par paquet, champs relus dans l'ordre et `p.Remaining.Should().Be(0)` ; parsers : `Revision.Parsers[9013].Parse(new ClientPacket(9013, body))` avec `"skill.mage.firebolt", 12, -1, -1`.

```csharp
[Fact]
public void SkillEvents_WriteImpactsThenOutcomes()
{
    (int header, ClientPacket p) = Write(new RpgSkillEventsMessageComposer
    {
        Impacts = [new RpgSkillImpact { CasterObjectId = 1, SkillId = "skill.archer.volley", TargetObjectId = 0, X = 6, Y = 4 }],
        Outcomes = [new RpgSkillOutcome { SourceObjectId = 1, SourceId = "skill.archer.volley", TargetObjectId = 2, Amount = 7, Absorbed = 0, Outcome = RpgSkillOutcome.Hit, Health = 33, MaxHealth = 40, Killed = false }],
    });

    header.Should().Be(9024);
    (p.PopInt(), p.PopInt(), p.PopString(), p.PopInt(), p.PopInt(), p.PopInt()).Should().Be((1, 1, "skill.archer.volley", 0, 6, 4));
    (p.PopInt(), p.PopInt(), p.PopString(), p.PopInt(), p.PopInt(), p.PopInt(), p.PopString(), p.PopInt(), p.PopInt(), p.PopBoolean())
        .Should().Be((1, 1, "skill.archer.volley", 2, 7, 0, "hit", 33, 40, false));
    p.Remaining.Should().Be(0);
}
```

- [ ] **Step 2 :** `dotnet test Vortex.Revisions.Tests --filter RpgSkillsWire` → échec de compilation.
- [ ] **Step 3 :** records, en-têtes (`RpgSkillCastMessageEvent = 9013`, … `RpgSkillEventsMessageComposer = 9024`), parsers (`PopString`, `PopInt` dans l'ordre), serializers (listes : `WriteInteger(count)` puis chaque élément ; `RpgStatuses` imbrique `k` statuts par combattant), `RpgSkillsMap` (3 `MapParser`, 7 `MapSerializer`).
- [ ] **Step 4 :** le filtre → vert ; `dotnet test Vortex.Revisions.Tests` → vert (`SerializerPairingTests`). `check-inert-declarations` montrera 3 messages sans handler et des composers non émis : attendu jusqu'aux Tâches 3 et 8 (ne pas mettre la base à jour).
- [ ] **Step 5 : commit** `compétences : le fil — 9013–9015 entrants, 9018–9024 sortants`.

---

### Task 3 : Le personnage — barre d'action, compétences connues, profil, statuts portés

**Files :**
- Create : `Vortex.Primitives/Rpg/Characters/CarriedStatus.cs`, `Vortex.Rpg.Characters/Grains/PlayerCharacterGrain.Skills.cs`, `Vortex.PacketHandlers/Rpg/Skills/RpgActionBarSetMessageHandler.cs`, `scripts/sql/2026-10-10-character-action-bar.sql`
- Modify : `Vortex.Database/Entities/Rpg/PlayerCharacterEntity.cs` (`[Column("action_bar")] [MaxLength(1024)] public string? ActionBar`), `InitialCreate` régénéré (skill `changing-the-schema`), `CharacterCombatProfile.cs` (+ `[Id(6)] string ClassId`, `[Id(7)] string SpecializationId`, `[Id(8)] ImmutableArray<CarriedStatus> Statuses = []`), `IPlayerCharacterGrain.cs` (`SetVitalsAsync(int health, int mana, ImmutableArray<CarriedStatus> statuses)`, `Task SetActionBarAsync(int slot, string actionId)`), `PlayerCharacterGrain.cs` (flush `.SetProperty(x => x.ActionBar, c.ActionBar)`), `PlayerCharacterGrain.Vitals.cs`, `PlayerCharacterGrain.Class.cs` (barre au choix de classe, dans le même `ExecuteUpdateAsync`), `PlayerCharacterGrain.Hud.cs` (`RpgSkills` dans `PushStateAsync` et au gain de niveau), `PlayerCharacterGrain.Progress.cs`, `CombatZone.cs` (`SetVitalsAsync(…, [])` en attendant la Tâche 5), tests qui appellent `SetVitalsAsync`
- Test : `Vortex.Database.Tests/Rpg/CharacterActionBarTests.cs` (SQLite, modèle `CharacterItemsPushTests`), `Vortex.PacketHandlers.Tests/Rpg/ActionBarSetHandlerTests.cs`

**Interfaces :**
- Consumes : `KnownSkills.For` (Tâche 1), `RpgSkillsMessageComposer`, `RpgSkillResultMessageComposer.BadSlot/UnknownSkill/NotLearned` (Tâche 2).
- Produces :
  - `public sealed record CarriedStatus { [Id(0)] string StatusId; [Id(1)] int RemainingMs; [Id(2)] int Stacks; [Id(3)] double Amount; }`.
  - `IPlayerCharacterGrain.SetActionBarAsync(int slot, string actionId)` : refus → `RpgSkillResult(actionId, code)` au joueur ; succès → `RpgSkills`. Une compétence déjà placée ailleurs est déplacée (son ancienne case se vide).
  - Barre stockée en JSON `["skill:skill.mage.firebolt","",…]` (longueur `ActionBarSize`) ; `null` en base = barre vide.
  - Statuts portés : `SetVitalsAsync(…, statuses)` les garde en mémoire (`_carried`, horodatés) ; `GetCombatProfileAsync` les rend avec leur durée restante diminuée du temps écoulé, sans ceux expirés, puis les oublie.

- [ ] **Step 1 : tests qui échouent** (`CharacterActionBarTests`) :
  - `ChoosingAClass_FillsTheBarWithItsKnownSkills` (mage niveau 1 → `["skill:skill.mage.firebolt","","","","",""]`, relu en base) ;
  - `ALevelUp_PlacesTheNewSkillInTheFirstEmptySlot` (niveau 2 → 3, `flame_burst` en case 1, `RpgSkills` poussé) ;
  - `SettingASlot_Persists_AndMovesASkillAlreadyPlaced` ;
  - `ASkillOfAnotherClass_IsRefused_WithNotLearned` ; `AnUnknownSkill_IsRefused` ; `ASlotOutOfRange_IsRefused_WithBadSlot` ; `AnEmptyAction_ClearsTheSlot` ;
  - `PushState_SendsTheSkills` ;
  - `TheProfile_CarriesClassAndSpecialization` ;
  - `CarriedStatuses_ComeBackOnce_WithTheirTimeLeft` (rendus avec 10 s, relus 4 s plus tard → 6 s ; relus une seconde fois → vides ; expirés → absents).
  - `ActionBarSetHandlerTests` : le handler appelle `SetActionBarAsync(slot, actionId)` sur le grain du joueur, rien sans `PlayerId`.
- [ ] **Step 2 :** `dotnet test Vortex.Database.Tests --filter CharacterActionBar` → échec.
- [ ] **Step 3 : implémenter.** Colonne + `InitialCreate` régénéré (`node .claude/skills/changing-the-schema/scripts/regenerate-initialcreate.mjs`), script SQL re-jouable sur le modèle de `2026-10-09-character-vitals.sql` (`vortex_add_character_column('action_bar', 'varchar(1024) NULL')`, ligne d'historique renommée vers le nouvel id). `PlayerCharacterGrain.Skills.cs` :

```csharp
public async Task SetActionBarAsync(int slot, string actionId)
{
    if (_character is not { } c)
    {
        return;
    }

    string skillId = actionId.StartsWith(SkillPrefix, StringComparison.Ordinal)
        ? actionId[SkillPrefix.Length..]
        : actionId;
    string? refusal =
        slot < 0 || slot >= _config.ActionBarSize ? RpgSkillResultMessageComposer.BadSlot
        : actionId.Length == 0 ? null
        : RefusalFor(c, actionId);

    if (refusal is not null)
    {
        await PushAsync(new RpgSkillResultMessageComposer { SkillId = skillId, Code = refusal });

        return;
    }

    string[] bar = Bar(c);
    int already = Array.IndexOf(bar, actionId);

    if (actionId.Length > 0 && already >= 0)
    {
        bar[already] = string.Empty;
    }

    bar[slot] = actionId;
    c.ActionBar = JsonSerializer.Serialize(bar);
    _dirty = true;

    await PushAsync(SkillsComposer(c));
}
```

(`_config` est `CharacterConfig` : ajouter `ActionBarSize = 6`, Décision 5 ; `SkillPrefix = "skill:"` ; `Bar(c)` relit le JSON et le complète à `ActionBarSize` cases.) Refus : `actionId` non vide qui ne commence pas par `skill:` ou dont la compétence n'existe pas → `unknown_skill` ; pas dans `KnownSkills.For(content, c.ClassId, c.SpecializationId, c.Level)` → `not_learned`. Choix de classe : barre = compétences connues dans l'ordre, complétée de `""`, écrite dans le même `ExecuteUpdateAsync` que la classe, puis `RpgSkills` poussé avec les stats (test `ChoosingAClass_PushesTheSkills`). Gain de niveau (`PushProgressAsync` quand `levelledUp`) : les compétences de niveau `oldLevel+1 … newLevel` absentes de la barre vont dans les premières cases vides, `_dirty = true`, `RpgSkills` ajouté aux composers poussés. Handler : `if (ctx.PlayerId <= 0) return; await grainFactory.GetPlayerCharacterGrain(ctx.PlayerId).SetActionBarAsync(message.Slot, message.ActionId);`.
- [ ] **Step 4 :** `dotnet test Vortex.Database.Tests`, `dotnet test Vortex.PacketHandlers.Tests`, `dotnet test Vortex.Rpg.Combat.Tests` (appel `SetVitalsAsync` adapté) → verts ; appliquer le script à `vortex_schema_check` (`apply-sql.ps1`).
- [ ] **Step 5 : commit** `compétences : barre d'action du personnage, compétences connues, profil et statuts portés`.

---

### Task 4 : Points d'extension de la zone de combat

**Files :**
- Create : `Vortex.Rpg.Combat/Systems/{ICombatSystem,ICastingSystem,CastRequest,CombatantExit}.cs`, `Vortex.Rpg.Combat/Stats/{IStatModifierSource,StatModifiers,CombatStat,CombatantBase}.cs`, `Vortex.Rpg.Combat/Hits/HitOptions.cs`, `Vortex.Rpg.Combat/Zones/CombatZone.Extensions.cs`
- Modify : `Combatants/Combatant.cs`, `Hits/{HitContext,HitResolver,DodgeStep,BaseDamageStep,CritStep}.cs`, `Zones/CombatZone.cs`, `Zones/CombatZone.Swings.cs`, `Zones/CombatZone.Outbox.cs`, `Vortex.Rpg/Zones/{IZoneSimulation,NoZoneSimulation,IZoneRuntime}.cs`, `Vortex.Rpg.Monsters/Zones/{RpgZoneSimulation,RpgZoneSimulationFactory}.cs`, les deux `Support/FakeZoneRuntime.cs`, `Vortex.Rooms/Grains/Zones/Runtime/RoomZoneRuntime.cs` (`SetMoveSpeed`, `SetRooted`), `IRoomAvatar` + `RoomAvatar` (`int MoveSpeedPercent { get; set; } = 100` ; le saut de pas qui le lit vient à la Tâche 8)
- Test : `Vortex.Rpg.Combat.Tests/CombatExtensionTests.cs`, `Vortex.Rpg.Monsters.Tests/StunnedMonsterTests.cs`, `Vortex.Rooms.Tests/Zones/RoomZoneRuntimeTests.cs` (`SetRooted_LocksAndStopsTheWalk`, `SetMoveSpeed_IsKeptOnTheAvatar`)

**Interfaces :**
- Consumes : `CharacterCombatProfile.ClassId/SpecializationId/Statuses` (Tâche 3).
- Produces :
  - `public interface ICombatSystem { int Order { get; } void Tick(CombatZone zone, long now); void Added(CombatZone zone, Combatant c, CharacterCombatProfile? profile) { } IEnumerable<CarriedStatus> Removing(CombatZone zone, Combatant c, CombatantExit exit) => []; void PlayerWalked(CombatZone zone, Combatant c) { } void BeforeFlush(CombatZone zone) { } IEnumerable<IComposer> Entry(CombatZone zone) => []; }` ; `enum CombatantExit { Died = 1, Left = 2 }`.
  - `public interface ICastingSystem : ICombatSystem { string? Cast(CombatZone zone, CastRequest request); void Cancel(CombatZone zone, PlayerId player); void Interrupt(CombatZone zone, Combatant c); }` ; `record CastRequest(PlayerId Player, string SkillId, int TargetObjectId, int X, int Y)`.
  - `public interface IStatModifierSource { void Contribute(Combatant c, StatModifiers into); }` ; `StatModifiers` : `AddFlat(CombatStat, double)`, `AddPercent(CombatStat, double)`, `double Apply(CombatStat stat, double baseValue)` = `(base + Σ plats) × (1 + Σ % / 100)`, `Slow(int percent)` (garde le plus fort), `int SlowPercent`, `bool Stunned { get; set; }`.
  - `enum CombatStat { Power = 1, Armor = 2, CritPercent = 3, DodgePercent = 4, SpeedPercent = 5, DamageDealtPercent = 6, DamageTakenPercent = 7 }`.
  - `public sealed record CombatantBase { int Power; int Armor; double CritPercent; double DodgePercent; int SpeedPercent = 100; }`.
  - `Combatant` : `CombatantBase Base`, `int MonsterIntervalMs` (intervalle de la définition, monstres), `double DamageDealtPercent`, `double DamageTakenPercent`, `int SlowPercent`, `bool Stunned`, `bool Busy` (une action l'occupe : pas de coup auto), `string ClassId = ""`, `string SpecializationId = ""`, `T? Get<T>() where T : class`, `void Set<T>(T? part) where T : class`.
  - `HitOptions { double? BaseDamage; bool Unavoidable; bool NoCrit; }` ; `HitResolver.Resolve(ICombatantStats a, ICombatantStats t, Random r, HitOptions? options = null)` → `(int Amount, int Absorbed, string Outcome)` ; `HitContext` gagne `Options` et `double Absorbed`.
  - `CombatZone` (public) : `IZoneRuntime Runtime`, `CombatConfig Config`, `Combatant? PlayerCombatant(PlayerId p)`, `void Recompute(Combatant c)`, `int Damage(Combatant source, Combatant target, int amount, long now)` (PV, dernier coup, menace × multiplicateur, participants si > 0, entrée en combat des deux, `_vitalsChanged`/`_selfChanged` ; retourne les PV restants), `int Heal(Combatant target, int amount)` (borné, retourne le soin réel), `void AddThreat(Combatant monster, int playerObjectId, long amount)`, `void Hostile(Combatant c, long now)` (rendu public), `void Broadcast(IComposer composer)`, `void SendTo(PlayerId player, IComposer composer)` (en file jusqu'à l'envoi du tick), `string? Cast(CastRequest request)` (→ `ICastingSystem` déclaré, sinon `unknown_skill`), `void CancelCast(PlayerId player)`, `void Interrupt(Combatant c)` (→ `ICastingSystem.Interrupt`, rien sans lui), `void PlayerWalked(PlayerId player)`, `bool IsSafeCell(int x, int y)`.
  - `CombatZone` constructeur : `+ IEnumerable<ICombatSystem>? systems = null, IEnumerable<IStatModifierSource>? modifiers = null` (en fin de liste).
  - `IZoneSimulation` : `Task CastSkillAsync(PlayerId player, string skillId, int targetObjectId, int x, int y, CancellationToken ct)` (envoie `RpgSkillResult` si refus), `void CancelCast(PlayerId player)`, `void PlayerWalked(PlayerId player)`.
  - `IZoneRuntime` (Tâche 8 l'implémente) : `void SetMoveSpeed(int objectId, int percent)`, `void SetRooted(int objectId, bool rooted)` ; les deux `FakeZoneRuntime` les enregistrent (`MoveSpeeds`, `Rooted`).

- [ ] **Step 1 : tests qui échouent** (`CombatExtensionTests`, faux système et fausse source déclarés dans le test) :
  - `Systems_TickInOrder_BeforeTheSwings` (ordres 200 et 100 → 100, 200, puis le coup auto) ;
  - `ABusyCombatant_DoesNotSwing` ; `AStunnedCombatant_DoesNotSwing` ;
  - `Modifiers_GiveEffectiveStats_FromTheBase` (base Power 20, +10 % et +2 plat → 24,2 arrondi 24 ; armure jamais négative ; crit borné à 100) ;
  - `ASlow_LengthensTheSwingInterval_AndSlowsTheWalk` (slow 60 → intervalle ×2,5 ; `MoveSpeeds[objectId] == 40`) ; `AStun_RootsTheAvatar` (`Rooted` contient l'objet ; levé au recalcul sans stun) ;
  - `DamageThroughTheZone_CountsLikeAHit` (participants, menace, en combat) ; `HealIsBounded` ;
  - `HitOptions_BaseDamageReplacesTheRoll_UnavoidableSkipsDodge_NoCritSkipsCrit` ;
  - `ASystem_SeesEntryWithTheProfile_AndRemovalWithTheExit` ; `CarriedStatuses_AreWrittenBackWithTheVitals` ;
  - `QueuedComposers_LeaveWithTheTick` ; `CastWithoutACastingSystem_IsUnknownSkill`.
  - `StunnedMonsterTests` : `AStunnedMonster_DoesNotThink` (pas de `WalkNear`, pas de cible posée) puis pense à nouveau une fois `Stunned` levé.
  - `RoomZoneRuntimeTests` : `SetRooted_LocksAndStopsTheWalk` (un loup en marche s'arrête, ne repart pas ; libéré, il marche) ; `UnrootingAGhost_KeepsItLocked` ; `SetMoveSpeed_IsKeptOnTheAvatar`.
- [ ] **Step 2 :** `dotnet test Vortex.Rpg.Combat.Tests` → échec de compilation.
- [ ] **Step 3 : implémenter.** `Apply(c, stats)` pose `c.Base` puis `Recompute(c)` ; `AddMonster` pose `Base = { Power = (min+max)/2, Armor, Crit, Dodge, SpeedPercent = 100 }`. `Recompute` :

```csharp
public void Recompute(Combatant c)
{
    StatModifiers m = new();

    foreach (IStatModifierSource source in _modifiers)
    {
        source.Contribute(c, m);
    }

    c.Power = Math.Max(0, (int)Math.Round(m.Apply(CombatStat.Power, c.Base.Power)));
    c.Armor = Math.Max(0, (int)Math.Round(m.Apply(CombatStat.Armor, c.Base.Armor)));
    c.CritPercent = m.Apply(CombatStat.CritPercent, c.Base.CritPercent);
    c.DodgePercent = m.Apply(CombatStat.DodgePercent, c.Base.DodgePercent);
    c.DamageDealtPercent = m.Apply(CombatStat.DamageDealtPercent, 0);
    c.DamageTakenPercent = m.Apply(CombatStat.DamageTakenPercent, 0);
    int speed = Math.Clamp((int)Math.Round(m.Apply(CombatStat.SpeedPercent, c.Base.SpeedPercent)), 50, 200);
    c.AttackIntervalMs = c.IsPlayer
        ? CombatMath.AttackIntervalMs(speed * (100 - m.SlowPercent) / 100, config) // plancher de CombatMath
        : c.MonsterIntervalMs * 100 / Math.Max(10, 100 - m.SlowPercent);

    if (c.SlowPercent != m.SlowPercent)
    {
        c.SlowPercent = m.SlowPercent;
        runtime.SetMoveSpeed(c.ObjectId, 100 - m.SlowPercent);
    }

    if (c.Stunned != m.Stunned)
    {
        c.Stunned = m.Stunned;
        runtime.SetRooted(c.ObjectId, m.Stunned);
    }
}
```

(`Combatant.MonsterIntervalMs` garde l'intervalle de la définition.) `Swings` saute `a.Busy || a.Stunned`. `Hit` passe par `Damage` (le message `RpgCombatHit` reste). `TickAsync` : `foreach (ICombatSystem s in _systems /* triés par Order */) s.Tick(this, now);` avant `Swings`. `FlushAsync` : `BeforeFlush` de chaque système, puis les messages de M1a, puis la file `Broadcast`/`SendTo`. `PlayerEnteredAsync` : `ClassId`, `SpecializationId`, puis `Added(this, c, profile)` de chaque système ; `PlayerStatsChanged` met à jour les deux ids. `PlayerLeftAsync` : `Removing(…, Left)` collecte les `CarriedStatus` passés à `SetVitalsAsync` ; mort : `Removing(…, Died)` (joueur au passage en fantôme, monstre à sa mort). `EntryVitals` ajoute `Entry(this)` de chaque système. `RpgZoneSimulation` : `CastSkillAsync` appelle `combat.Cast(…)` et envoie `RpgSkillResult(skillId, code)` au refus ; ne fait pas penser un monstre `Stunned` ; la fabrique reçoit `IEnumerable<ICombatSystem>` et `IEnumerable<IStatModifierSource>` et les passe à `CombatZone`. `NoZoneSimulation` : sans zone de combat, les trois méthodes ne font rien (`Task.CompletedTask`). `RoomZoneRuntime.SetMoveSpeed` pose `MoveSpeedPercent` ; `SetRooted(true)` = `SetMovementLocked(true)` + `CancelWalk`, `SetRooted(false)` déverrouille sauf un fantôme couché (`HasStatus(Lay)`).
- [ ] **Step 4 :** `dotnet test Vortex.Rpg.Combat.Tests` et `dotnet test Vortex.Rpg.Monsters.Tests` → verts (les tests M1a inchangés passent : régression nulle) ; `dotnet test Vortex.Rooms.Tests` → vert.
- [ ] **Step 5 : commit** `combat : points d'extension — systèmes de tick, stats effectives, options de coup, opérations partagées`.

---

### Task 5 : Les statuts

**Files :**
- Create : `Vortex.Rpg.Skills/Statuses/{ActiveStatus,StatusSet,StatusSystem,StatusApplier}.cs`, `Vortex.Rpg.Skills/Hits/{DamageModifierStep,ShieldStep}.cs`, `Vortex.Rpg.Skills/Zones/SkillEvents.cs` (partie de zone : impacts et résultats du tick), `Vortex.Rpg.Skills.Tests/Support/{SkillWorld,FakeZoneRuntime,FixedRandom}.cs`
- Modify : `SkillsModule.cs` (`ICombatSystem`, `IStatModifierSource` → `StatusSystem` ; `IHitStep` → les deux étapes ; `StatusApplier` singleton)
- Test : `Vortex.Rpg.Skills.Tests/StatusTests.cs`

**Interfaces :**
- Consumes : Tâche 4 entière ; `StatusDefinition` (Tâche 1) ; `RpgStatusesMessageComposer`, `RpgSkillEventsMessageComposer` (Tâche 2).
- Produces :
  - `ActiveStatus { StatusDefinition Definition; int CasterObjectId; long AppliedAtMs; long EndsAtMs; long NextPulseAtMs; int Stacks; double Amount; /* par pulsation ou absorption restante */ long Sequence; }`.
  - `StatusSet` (partie de `Combatant`) : `IReadOnlyList<ActiveStatus> All`, `bool Changed`, `double Absorb(double damage)` (consomme les boucliers, du plus ancien au plus récent, retire ceux épuisés, `Changed = true`).
  - `StatusApplier.Apply(CombatZone zone, Combatant caster, Combatant target, StatusDefinition status, long now)` → bool posé (règles de cumul spec §7.1, plafond `MaxStatusesPerCombatant` : retire le plus ancien) ; `Remove(…)`, `Dispel(CombatZone zone, Combatant target, bool buffs, int count)`.
  - `SkillEvents` (partie de zone, `zone.Get<SkillEvents>()`) : `AddImpact(RpgSkillImpact)`, `AddOutcome(RpgSkillOutcome)` ; vidée dans `BeforeFlush` en un `RpgSkillEventsMessageComposer`.
  - `StatusSystem` : ordre 100 ; `Tick` (expirations, pulsations `dot` → `zone.Damage` avec `HitOptions { BaseDamage = Amount × Stacks, Unavoidable = true, NoCrit = true }`, `hot` → `zone.Heal` + menace des soins) ; `Contribute` (modificateurs, `Slow`, `Stunned`) ; `Removing` (mort : tout tombe ; sortie : renvoie les persistants en `CarriedStatus`) ; `Added` (réapplique `profile.Statuses`) ; `BeforeFlush` (un `RpgStatuses` des combattants `Changed`, puis `SkillEvents`) ; `Entry` (`RpgStatuses` de ceux qui en portent).
  - `DamageModifierStep` (350) : `Damage × (1 + attacker.DamageDealtPercent/100) × (1 + target.DamageTakenPercent/100)`, plancher 0 ; `ShieldStep` (950) : `hit.Absorbed = set.Absorb(hit.Damage); hit.Damage -= hit.Absorbed`.
  - Menace des soins : `HealThreat.Spread(CombatZone zone, Combatant healer, Combatant healed, int healed, SkillsConfig config)` (classe statique dans `Statuses/`, réutilisée par la Tâche 6).

- [ ] **Step 1 : tests qui échouent** (`StatusTests`, `SkillWorld` = une `CombatZone` sur `FakeZoneRuntime` avec `StatusSystem`, les étapes et un joueur mage à (5,5), un loup à (6,5)) :
  - cumul : `Replace_RecomputesTheAmount`, `Refresh_RestartsTheDuration_KeepsTheAmount`, `Stack_AddsUpToMax_AndMultipliesThePulse`, `Ignore_KeepsTheFirst` ; `PastTheCap_TheOldestGoes` ;
  - types : `ADot_PulsesOnItsPeriod_AndGivesThreatAndParticipation`, `AHot_HealsOnItsPeriod_AndSpreadsHealThreat`, `ABuff_RaisesCrit_UntilItExpires`, `ADebuff_LowersDamageDealt`, `ASlow_UsesTheStrongest`, **`AStun_StopsSwingsAndWalk_ThenEverythingResumes`** (Review Focus 3 : coups arrêtés, `Rooted`, levé à l'expiration, coups qui reprennent), **`AShield_AbsorbsAfterModifiersAndCrit_ThenBreaks`** (Review Focus 5 : `damage_taken_percent` +50 %, critique, bouclier 20 : absorbé = 20 pile, statut retiré, l'attaque de base suivante n'est plus absorbée ; `RpgCombatEvents` de l'attaque de base montre le montant après absorption) ;
  - fin : `Death_DropsEveryStatus`, `Leaving_DropsTheRest_AndCarriesThePersistent`, `ACarriedStatus_IsReappliedOnEntry_WithItsTimeLeft` ;
  - envois : `OneStatusesMessagePerTick_ForTheChangedOnly`, `TheEntryBurst_CarriesStatuses`.
- [ ] **Step 2 :** `dotnet test Vortex.Rpg.Skills.Tests --filter StatusTests` → échec de compilation.
- [ ] **Step 3 : implémenter** (Puissance du lanceur au moment de la pose : `Amount = Base + Coefficient × caster.Power`, gardé dans `ActiveStatus`). Le cœur du cumul :

```csharp
switch (status.Stacking)
{
    case StatusStacking.Ignore when existing is not null:
        return false;
    case StatusStacking.Refresh when existing is not null:
        existing.EndsAtMs = now + status.DurationMs;
        break;
    case StatusStacking.Stack when existing is not null:
        existing.Stacks = Math.Min(status.MaxStacks, existing.Stacks + 1);
        existing.EndsAtMs = now + status.DurationMs;
        break;
    default: // Replace, or a first application
        if (existing is not null) { set.Remove(existing); }
        set.Add(New(status, caster, now));
        break;
}

set.Changed = true;
zone.Recompute(target);
```

  Un débuff ou un `dot` posé sur un monstre : `zone.Hostile(caster, now)`, `zone.Hostile(target, now)`, menace 1 (`AddThreat`).
- [ ] **Step 4 :** `dotnet test Vortex.Rpg.Skills.Tests` → vert ; `dotnet test Vortex.Rpg.Combat.Tests` → vert.
- [ ] **Step 5 : commit** `compétences : les effets de statut — cumul, sept types, bouclier, modificateurs, fin et statuts portés`.

---

### Task 6 : Les types d'effet et le ciblage

**Files :**
- Create : `Vortex.Rpg.Skills/Effects/{ISkillEffect,SkillContext}.cs`, `Vortex.Rpg.Skills/Targeting/{SkillTargets,TargetCheck}.cs`
- Modify : les 6 effets de la Tâche 1 (`Apply`), `SkillsModule.cs` (chaque effet aussi en `ISkillEffect`)
- Test : `Vortex.Rpg.Skills.Tests/EffectTests.cs`, `TargetingTests.cs`

**Interfaces :**
- Consumes : Tâches 4 et 5.
- Produces :
  - `public interface ISkillEffect : ISkillEffectRule { void Apply(SkillContext ctx, Combatant target, SkillEffectDefinition effect); }`.
  - `SkillContext { CombatZone Zone; Combatant Caster; SkillDefinition Skill; long Now; Random Random; HitResolver Hits; StatusApplier Statuses; SkillsConfig Config; IRpgContent Content; Dictionary<int, int> DamageDone; /* par cible, pour l'effet threat */ }`.
  - `SkillTargets.Resolve(SkillContext ctx, int targetObjectId, (int X, int Y)? cell)` → `IReadOnlyList<Combatant>` : `self` → lanceur ; `ally`/`enemy` → la cible si vivante ; zones → vivants du camp (offensif : monstres non invulnérables ; sinon joueurs) dans `Radius` (Chebyshev) du centre, ligne de vue depuis le centre, triés par distance puis id, `MaxTargets` au plus. Camp offensif = le premier effet est `damage`, `interrupt`, `threat` ou `apply_status` d'un `debuff`/`dot`/`stun`/`slow`.
  - `TargetCheck.Validate(SkillDefinition skill, Combatant caster, Combatant? target, (int X, int Y)? cell, CombatZone zone)` → code ou null (`invalid_target`, `safe_zone`) — utilisé par la Tâche 7.
  - Effets : `damage` → `Hits.Resolve(caster, target, Random, new HitOptions { BaseDamage = Base + Coefficient × caster.Power, Unavoidable = Skill.Unavoidable })`, `zone.Damage`, résultat `hit`/`crit`/`dodge` dans `SkillEvents`, `DamageDone[target] += amount` ; `heal` → crit possible (jet `CritPercent`, × `CritMultiplier`), `zone.Heal`, `HealThreat.Spread`, résultat `heal` ; `apply_status` → `Statuses.Apply` ; `interrupt` → `zone.Interrupt(target)` (`RpgCastEnd(interrupted)` ; sans effet sur un monstre en M1b, qui ne lance rien) ; `dispel` → `Statuses.Dispel` (débuffs sur un allié, buffs sur un ennemi, `Amount` au plus, les plus récents d'abord) ; `threat` → `AddThreat(target, caster, DamageDone[target] × ThreatPercent / 100)`.
  - Une cible tuée par un effet ne reçoit pas les suivants (`target.IsDead` vérifié entre deux effets).
  - Un `kind` d'effet sans `ISkillEffect` déclaré, ou un statut absent du contenu au moment d'appliquer (pack remplacé, spec §12) : effet ignoré, **une** alerte par id (ensemble des ids déjà signalés) — test `AnUndeclaredEffect_IsSkipped_WithOneWarning`.

- [ ] **Step 1 : tests qui échouent** : `EffectTests` — un test par effet (montant exact avec `FixedRandom` : Trait de feu, P 25 → 8 + 45 = 53, armure 2 → 51,96 → 52) ; `AHealCrit_IsOneAndAHalf` ; `ThreatEffect_AddsAPercentOfThisSkillsDamage` ; `ADeadTarget_GetsNoFurtherEffect` ; `Dispel_TakesTheNewestDispellableFirst`. `TargetingTests` — chaque mode ; `AnArea_HitsTheNearestFirst_UpToMaxTargets` ; `AnArea_SkipsTheDead_AndThoseOutOfSight` ; `AnOffensiveArea_NeverHitsPlayers` ; `AHealingArea_NeverHitsMonsters` ; `AnOffensiveSkill_FromOrOntoASafeCell_IsRefused` ; `AHeal_IsAllowedOnASafeCell` ; `AllyZero_IsTheCaster`.
- [ ] **Step 2 :** `dotnet test Vortex.Rpg.Skills.Tests --filter "EffectTests|TargetingTests"` → échec.
- [ ] **Step 3 :** implémenter.
- [ ] **Step 4 :** `dotnet test Vortex.Rpg.Skills.Tests` → vert.
- [ ] **Step 5 : commit** `compétences : les six types d'effet et le ciblage`.

---

### Task 7 : Le lancer — contrôles, approche, incantation, canalisation, annulations, recharges

**Files :**
- Create : `Vortex.Rpg.Skills/Casting/{CastSystem,CastState,PendingCast,SkillCooldowns}.cs`
- Modify : `SkillsModule.cs` (`CastSystem` en `ICombatSystem` et `ICastingSystem`) ; `Vortex.Primitives/Observability/IVortexMetrics.cs` + son implémentation (`SkillCast(string skillId, string outcome)` — `resolved`, `cancelled`, `interrupted`, `failed` ou le code de refus ; `StatusApplied(string statusId)`, appelé par `StatusApplier`) — spec §13
- Test : `Vortex.Rpg.Skills.Tests/CastTests.cs`, `CastCancelTests.cs`, `StartingSkillsTests.cs`

**Interfaces :**
- Consumes : Tâches 4 à 6 ; `KnownSkills.For` ; `RpgCastStart*`, `RpgCastEnd*`, `RpgCooldownsMessageComposer`, `RpgSkillResultMessageComposer` (Tâche 2).
- Produces :
  - `SkillCooldowns` (partie) : `long GlobalUntilMs`, `Dictionary<string, long> Until`.
  - `CastState` (partie) : `SkillDefinition Skill`, `int TargetObjectId`, `(int X, int Y)? Cell`, `long StartedAtMs`, `long EndsAtMs`, `long NextPulseAtMs`, `bool Channel` ; `PendingCast` (partie) : la même demande en attente d'approche, ou d'un tick pour une instantanée.
  - `CastSystem` (ordre 200) :
    - `Cast(zone, request)` : contrôles dans l'ordre exact (Global Constraints) ; hors portée ou de vue → `PendingCast` + `WalkNear(caster, cible ou case, Range)` (`NoPath` → `unreachable`) ; en portée → instantanée : `PendingCast` résolue au prochain `Tick` ; incantée : `CastState`, `Busy = true`, délai global posé, `RpgCastStart` ; canalisée : mana et cooldown pris, `CastState`, `Busy = true`, `RpgCastStart`.
    - `Tick` : pending (en portée et en vue → revérifie cooldown, délai global, mana, puis résout ; cible morte/partie → abandon `unreachable`) ; incantations finies (revérifie portée et vue → sinon `RpgCastEnd(failed)` + `RpgSkillResult(unreachable|no_line_of_sight)` ; revérifie la mana → sinon `failed` + `no_mana` ; prend la mana, pose le cooldown, applique, `RpgCastEnd(done)`) ; canalisations (pulsation tous les `PulseMs`, fin → `done`) ; une cible unique morte ou partie → `cancelled`.
    - Résoudre = `SkillTargets.Resolve` puis chaque effet de chaque cible ; un `RpgSkillImpact` par application ; puis `RpgCooldowns` au lanceur.
    - `PlayerWalked` : incantation sans `MoveAllowed` → `cancelled` ; `PendingCast` abandonnée sans message.
    - `Cancel(zone, player)` (paquet 9015) → `cancelled`.
    - `Removing` (mort, sortie) → `cancelled` ; `Interrupt` (effet `interrupt`) et un lanceur trouvé `Stunned` au tick → `interrupted`.
    - Annuler = `Busy = false`, `CastState` retirée, `RpgCastEnd`, rien pris pour une incantation (le délai global déjà posé reste).
  - Les `RpgCastStart`/`RpgCastEnd` du tick sont regroupés et envoyés dans `BeforeFlush`.

- [ ] **Step 1 : tests qui échouent.**
  - `CastTests` : un test par code de refus, dans l'ordre (un lanceur mort ET sans mana → `dead`) ; `AnInstant_LandsOnTheNextTick` ; `ACast_LandsAfterItsTime_ThenTakesManaAndCooldown` ; `ACastEndingOutOfRange_Fails_WithoutCost` ; `AChannel_PulsesThenEnds_AndPaidUpFront` ; `TheAutoAttack_PausesDuringACast_ThenResumes` ; `OutOfRange_ApproachesThenCasts` ; `NoPath_IsUnreachable` ; `GlobalCooldown_IsShared_ButNotByAnOffGcdSkill` ; `TheCooldown_StartsAtResolution` ; **`ACastSpammedDuringItsGlobalCooldown_ResolvesOnce`** (Review Focus 4 : 5 paquets dans le même tick → un seul impact, une seule mana prise, 4 refus `global_cooldown` ou `casting`) ; `CooldownsAreSentAfterEachCast`.
  - `CastCancelTests` : `WalkingCancels_UnlessMoveAllowed` ; `CastCancelPacket_Cancels` ; **`AStunMidCast_Interrupts`** (Review Focus 3) ; `AnInterruptEffect_Interrupts` ; **`TheTargetDyingMidCast_CancelsTheCast`** (Review Focus 2) ; **`LeavingMidCast_CancelsWithoutCost_AndLandsNothing`** (Review Focus 1 : `PlayerLeftAsync` pendant une incantation de 2 s, puis 3 s de ticks → pas de dégâts, mana intacte dans `SetVitalsAsync`, `RpgCastEnd(cancelled)`) ; `DyingMidChannel_KeepsWhatLanded` ; `AWalkCancelsAPendingApproach_Silently`.
  - `StartingSkillsTests` : charge `content/` du dépôt (comme `ContentPackInRepoTests`) et lance chacune des 12 compétences dans un `SkillWorld` avec un personnage de la bonne classe au niveau 6 : l'effet attendu de spec §16 est observé (dégâts, statut posé avec la bonne règle, soin, bouclier, ralentissement, étourdissement, zone sur le sol).
- [ ] **Step 2 :** `dotnet test Vortex.Rpg.Skills.Tests --filter "Cast|StartingSkills"` → échec.
- [ ] **Step 3 :** implémenter.
- [ ] **Step 4 :** `dotnet test Vortex.Rpg.Skills.Tests` → vert ; `dotnet test Vortex.Rpg.Combat.Tests`, `Vortex.Rpg.Monsters.Tests` → verts.
- [ ] **Step 5 : commit** `compétences : le lancer — contrôles, approche, incantation, canalisation, annulations, recharges`.

---

### Task 8 : La room — marche ralentie, immobilisation, paquets de lancer, rafale d'entrée

**Files :**
- Create : `Vortex.PacketHandlers/Rpg/Skills/{RpgSkillCastMessageHandler,RpgCastCancelMessageHandler}.cs`
- Modify : `Vortex.Rooms/Grains/Systems/RoomAvatarTickSystem.cs` (saut de pas selon `MoveSpeedPercent`, posé par la Tâche 4), `Vortex.Primitives/Rooms/Grains/IRoomCombat.cs` (`CastSkillAsync(ActionContext ctx, string skillId, int targetObjectId, int x, int y, CancellationToken ct)`, `CancelCastAsync(ActionContext ctx, CancellationToken ct)`), `Vortex.Rooms/Grains/RoomGrain.Combat.cs`, `Vortex.Rooms/Grains/Modules/RoomAvatarModule.Walk.cs` (`Simulation.PlayerWalked(ctx.PlayerId)` à côté de `StopAttack`), `scripts/hooks/authorization-surface-baseline.json` (`--update`)
- Test : `Vortex.Rooms.Tests/Zones/RoomSkillsTests.cs`, `Vortex.Rooms.Tests/Avatars/SlowedWalkTests.cs`, `Vortex.PacketHandlers.Tests/Rpg/SkillCastHandlerTests.cs`

**Interfaces :**
- Consumes : `IZoneSimulation.CastSkillAsync/CancelCast/PlayerWalked`, `IZoneRuntime.SetMoveSpeed/SetRooted` (Tâche 4).
- Produces : un avatar à `MoveSpeedPercent = p` ne fait un pas que si `now ≥ NextMoveStepAtMs`, puis `NextMoveStepAtMs = now + AvatarTickMs × 100 / p − AvatarTickMs` (à 100 : chaque tick, comme avant).

- [ ] **Step 1 : tests qui échouent.**
  - `SlowedWalkTests` : `AtFortyPercent_AnAvatarStepsTwoAndAHalfTimesSlower` (5 cases à 100 % en 5 ticks ; à 40 % ≈ 12 ticks) ; `AtFullSpeed_NothingChanges` ; `ARootedAvatar_DoesNotWalk_AndIsFreedAfter`.
  - `RoomSkillsTests` (simulation qui enregistre, comme `RoomCombatTests`) : `ACastPacket_GoesToTheSimulation` ; `CastCancel_GoesToTheSimulation` ; `AManualWalk_TellsTheSimulation` ; `WithoutAnAvatarHere_NothingIsCast` ; `AnInvalidActor_IsRefused`.
  - `SkillCastHandlerTests` : handler → `GetRoomCombat(ctx.RoomId).CastSkillAsync(ctx.AsActionContext(), "skill.mage.firebolt", 12, -1, -1, ct)` ; rien sans `PlayerId` ou `RoomId`.
- [ ] **Step 2 :** `dotnet test Vortex.Rooms.Tests --filter "RoomSkills|SlowedWalk"` → échec.
- [ ] **Step 3 :** implémenter ; `node scripts/hooks/check-authorization-surface.mjs --update` (deux méthodes à acteur, garde `ctx.PlayerId <= 0`) ; `check-inert-declarations` → 0 (les 3 entrants ont leur handler, les 7 sortants sont émis).
- [ ] **Step 4 :** `dotnet test Vortex.Rooms.Tests`, `Vortex.PacketHandlers.Tests`, `Vortex.Hosting.Tests -o Vortex.Hosting.Tests/bin/alt` → verts.
- [ ] **Step 5 : commit** `compétences : la room — marche ralentie, immobilisation, lancer et annulation par paquet`.

---

### Task 9 : La preuve `skills`, la documentation, la porte

**Files :**
- Modify : `.claude/skills/run-vortex-emulator/driver.mjs` (en-têtes `rpgSkillCast: 9013, rpgActionBarSet: 9014, rpgCastCancel: 9015, rpgSkills: 9018, rpgSkillResult: 9019, rpgCastStart: 9020, rpgCastEnd: 9021, rpgCooldowns: 9022, rpgStatuses: 9023, rpgSkillEvents: 9024` ; `const PLAYER_C = { email: 'run-skill-c@vortex.local', password: 'RunSkill!2026' }` ; `cmdSkills` ; `skills: cmdSkills`), `.claude/skills/run-vortex-emulator/SKILL.md`, `docs/architecture.md` (« Flow 12 — skills »), `AGENTS.md` (ligne « A skill effect, a status rule, a combat system »), spec M1b (statut « livrée »)

- [ ] **Step 1 : `cmdSkills`.** Pousse le contenu, réutilise les zones de `cmdCombat` (`proof_combat` publiée si absente), connecte `PLAYER_C` ; à l'écran des personnages, si son personnage est `class.classless`, envoie `rpgChooseClass(playerId, 'class.mage')` (comme `cmdLobby`), puis `rpgEnterWorld`. Étapes, chacune `die(...)` si l'attendu manque :
  1. `RpgSkills` : `skill.mage.firebolt` connu et en case 0 ;
  2. marche hors du camp à portée d'un loup ; `RpgSkillCast('skill.mage.firebolt', loup, -1, -1)` → `RpgCastStart` (2000 ms, `channel` faux) → `RpgSkillEvents` (résultat sur le loup, montant > 0) → `RpgCooldowns` ;
  3. deux `RpgSkillCast` coup sur coup → le second `RpgSkillResult('skill.mage.firebolt', 'casting')` ou `global_cooldown` ;
  4. `RpgSkillCast` puis une marche → `RpgCastEnd(cancelled)` ;
  5. retour au camp ; `RpgSkillCast` sur un loup → `RpgSkillResult(…, 'safe_zone')`.
  Puis `log('Skills OK.')`.
- [ ] **Step 2 : en direct** sur l'instance `alt` (script `amb-alt.ps1`, base `vortex_schema_check` migrée par le script de la Tâche 3) : `start`, `skills`, `combat`, `smoke` → OK ; `stop`.
- [ ] **Step 3 : docs.** `architecture.md` : flux 12 (paquet → room → simulation → `CastSystem` ; statuts au tick ; étapes 350 et 950 ; barre au personnage) et l'entrée d'index ; `AGENTS.md` : « A skill effect / a combat system | an `ISkillEffect` (key) in `Vortex.Rpg.Skills/Effects` or an `ICombatSystem` (order), declared by DI in its module | `Effects/DamageEffect.cs`, `Statuses/StatusSystem.cs` » ; `SKILL.md` : ligne `skills`.
- [ ] **Step 4 : porte complète** pas à pas, au premier plan (script de porte avec `Rpg.Skills` dans la liste des tests) : chaque étape à 0.
- [ ] **Step 5 : commit** `compétences : preuve driver skills, documentation`. Puis `task-done` et la relecture finale de la branche (opus) avec le Review Focus ci-dessus.
