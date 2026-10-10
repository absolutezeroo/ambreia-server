# RPG M1c — Monstres avancés : plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **Mode choisi par le propriétaire :** Claude implémente chaque tâche, un relecteur passe toutes les 2 tâches, puis une relecture finale (opus). Spec approuvée le 2026-10-10 (« Nickel »).

**Goal :** des monstres qui tirent de loin et lancent des compétences, un élite, un boss à phases (compétences, statut, renforts, cri) qui annonce ses attaques par des zones au sol et se remet à zéro.

**Architecture :**
- **Le cœur `Vortex.Rpg`** gagne les champs de contenu (rang, règles d'usage, phases, télégraphe) et leur validation ; `IMonsterBehaviorKey` laisse `MonsterKind` refuser un comportement qu'aucun module ne déclare.
- **`Vortex.Rpg.Combat`** : `CastRequest` désigne son lanceur par id d'objet ; `TelegraphCells` (géométrie pure) ; `RankScale` et `Combatant.Immunities` ; `IStatusSystem` (poser, retirer un statut depuis un autre module) et `ICastingSystem.Reset`.
- **`Vortex.Rpg.Skills`** : `CastSystem` sert tout lanceur, porte les télégraphes ; `StatusSystem` implémente `IStatusSystem`, les statuts sans fin et les immunités ; camps relatifs au lanceur.
- **`Vortex.Rpg.Monsters`** : base `MonsterBehavior`, `ranged`, `caster`, `MonsterSkillSelector`, `BossPhases`, renforts, `MonstersConfig`. Il ne référence toujours pas `Vortex.Rpg.Skills` : il passe par `CombatZone.Cast`, `ApplyStatus`, `RemoveStatus`, `ResetCasts`.

**Tech Stack :** C# .NET 10, Orleans, xUnit + FluentAssertions, `FakeZoneRuntime` (Skills.Tests, Monsters.Tests), `SkillWorld`, `MonsterWorld`.

**Spec :** `docs/superpowers/specs/2026-10-10-rpg-monsters-m1c-design.md` (lire aussi M1a et M1b).

## Global Constraints

- **Ids réseau :** sortants **9025 `RpgTelegraphs`**, **9026 `RpgMonsterYell`** ; **9009 `RpgTarget`** gagne `rank` (chaîne) **en dernier champ** ; `RpgSkillOutcome.Immune = "immune"`. Aucun entrant nouveau.
- **Ordre des champs 9025** : nombre d'entrées, puis pour chacune `casterObjectId` (int), `skillId` (string), `shape` (string `circle`/`line`/`cone`), `delayMs` (int), nombre de cases, puis `x`, `y` (int) par case. **9026** : `objectId` (int), `textKey` (string).
- **Statut sans fin** : `RpgStatusEntry.RemainingMs = -1`.
- **`Vortex:Monsters`** : `Ranks:elite` (PV ×3, dégâts ×1,5, XP ×3, immunités `[]`), `Ranks:boss` (PV ×10, dégâts ×2, XP ×10, immunités `["stun", "slow"]`), `MaxSummonsPerMonster` 16, `RangedAimMs` 250. Valeur invalide → démarrage refusé, message qui nomme la clé.
- **Bornes de contenu** (spec §5.4) : `chance` et `belowHealthPercent` 1–100 ; `atHealthPercent` 1–99 sans doublon ; `count` 1–8 ; télégraphe `size` 1–10, `width` impair 1–5, `angle` 30–180, compétence `cast` seulement.
- **Couches :** `Vortex.Rpg.Monsters` ne référence pas `Vortex.Rpg.Skills` (ni ses tests) ; `Vortex.Rooms` ne référence que le cœur.
- **Champs de contenu ajoutés facultatifs avec défaut** (AGENTS.md, Gotchas) ; aucune table, aucune colonne.
- **Disposition :** un type par fichier ; le namespace suit le dossier ; un dossier = une sorte de chose.
- **Commits** en français, chemins explicites, terminés par les deux lignes `Co-Authored-By` / `Claude-Session` ; jamais `scripts/sql/import_reference_data.sql`, `docs/superpowers/specs/2026-10-06-vortex-mmorpg-master-design.md`, ni les fichiers d'une autre session (`ZoneEditorService*`, `ZoneSummary`, `openapi.json` s'il porte leur bloc).
- **Portes :** `Vortex.Main/bin/Debug` peut être verrouillé par l'émulateur du propriétaire : script de porte pas à pas (Main et Hosting.Tests en `-o …/bin/alt`), au premier plan.

## Décisions de ce plan (écarts assumés à la spec)

1. **Phases vérifiées au début du tick de la simulation**, pas « après chaque coup » : les coups tombent dans `CombatZone.TickAsync`, après la pensée des monstres ; le tick suivant (≤ 250 ms) voit les PV et joue les phases. Même effet observable, aucun crochet dans la chaîne de coup.
2. **Refus d'un monstre non comptés** dans `SkillCast` : le sélecteur essaie à chaque tick, compter ses `cooldown` noierait la métrique. Les fins (`resolved`, `interrupted`, `cancelled`) le sont.
3. **Le sélecteur s'appuie sur le refus de `Cast`** pour les recharges, le délai global et la portée : `Vortex.Rpg.Monsters` ne voit pas les recharges (elles vivent dans `Vortex.Rpg.Skills`). Il filtre lui-même ce qui est gratuit (cible, `minRange`, PV, `chance`), puis essaie ; un refus passe à la règle suivante.
4. **Ligne et cône exigent une direction** : refusés à l'import pour les modes `self` et `around_caster` (la spec retombait sur l'orientation, que `IZoneRuntime` ne lit pas). Code `TelegraphNeedsAim`.
5. **`caster` se tient à la plus petite portée** de ses compétences offensives (`enemy`, `ground`, `around_target`) de la liste courante, sinon `attackRange` : à cette distance il peut toutes les lancer.
6. **Règle « compétence dont un effet n'a pas de règle »** (spec §5.4) : déjà refusée par `SkillKind` sur la compétence elle-même ; `MonsterKind` ne la répète pas.

## Review Focus

1. **Le boss meurt (ou rentre) pendant un télégraphe** : `RpgCastEnd(cancelled)`, aucun impact ensuite, aucune exception ; à sa mort les renforts restent. Test : Tâche 5, `ACasterDyingMidTelegraph_LandsNothing` ; Tâche 9, `TheBossDying_LeavesItsSummons`.
2. **Un joueur sur une case sûre dans les cases d'un télégraphe** : il n'est pas touché (pas de combat sur case sûre, M1a). Test : Tâche 5, `APlayerOnASafeCell_IsNotHitByATelegraph`.
3. **Remise à zéro en phase 2, puis nouvel engagement** : statut de phase retiré, renforts partis, liste d'origine ; la phase 2 se rejoue (cri et renforts à nouveau). Test : Tâche 9, `AReset_LetsThePhasesPlayAgain`.
4. **Le chaman ne soigne ni un mort, ni un monstre qui rentre (invulnérable), ni un joueur** ; il se soigne lui-même s'il est le plus blessé. Test : Tâche 7, `AnAllySkill_PicksTheMostWoundedLiveMonster`.
5. **Un renfort tué ne réapparaît jamais et libère sa place sous le plafond.** Test : Tâche 9, `AKilledSummon_NeverRespawns_AndFreesItsSlot`.

---

### Task 1 : Contenu — rang, règles d'usage, phases, télégraphe, validation

**Files :**
- Create (cœur) : `Vortex.Rpg/Content/Definitions/{MonsterRank,MonsterSkillRule,MonsterPhase,MonsterSummon,SkillTelegraph,TelegraphShape}.cs`, `Vortex.Rpg/Content/Rules/IMonsterBehaviorKey.cs`
- Modify : `Vortex.Rpg/Content/Definitions/{MonsterDefinition,SkillDefinition}.cs`, `Vortex.Rpg/Content/Kinds/{MonsterKind,SkillKind}.cs`, `Vortex.Rpg.Monsters/Behaviors/IMonsterBehavior.cs`, `Vortex.Rpg.Monsters/MonstersModule.cs`, `Vortex.Hosting.Tests/Rpg/{ContentSchemaTests,ContentPackInRepoTests}.cs`, `Vortex.Rpg.Tests/Content/MonsterKindTests.cs`, `schemas/content/{monster,skill}.schema.json` (régénérés)
- Test : `Vortex.Rpg.Tests/Content/MonsterKindTests.cs`, `Vortex.Rpg.Skills.Tests/Content/SkillKindTests.cs`

**Interfaces :**
- Produces :
  - `enum MonsterRank { Normal = 1, Elite = 2, Boss = 3 }` ; `enum TelegraphShape { Circle = 1, Line = 2, Cone = 3 }` (JSON snake_case par `ContentJson`).
  - `record MonsterSkillRule { required string Skill; int Priority; int MinRange; int BelowHealthPercent = 100; int Chance = 100; }`
  - `record MonsterSummon { required string Monster; int Count = 1; int Level; }` (`Level` 0 : celui du boss).
  - `record MonsterPhase { required int AtHealthPercent; List<MonsterSkillRule> Skills = []; string? Status; MonsterSummon? Summon; string? Yell; }`
  - `MonsterDefinition` : `MonsterRank Rank = Normal`, `List<MonsterSkillRule> Skills = []`, `List<MonsterPhase> Phases = []`.
  - `record SkillTelegraph { required TelegraphShape Shape; required int Size; int Width = 1; int Angle = 90; }` ; `SkillDefinition.Telegraph` (`SkillTelegraph?`, défaut null). `Classes` reste requis mais peut être vide.
  - `public interface IMonsterBehaviorKey { string Key { get; } }` (`Vortex.Rpg.Content.Rules`) ; `IMonsterBehavior : IMonsterBehaviorKey`.
  - `MonsterKind(IEnumerable<IMonsterBehaviorKey> behaviors)`.

- [ ] **Step 1 : tests qui échouent.** `MonsterKindTests` (construit avec les clés `melee`, `ranged`, `caster`) : `UnknownBehavior` (`"flying"`) ; `BrokenReference` (règle vers une compétence inconnue, statut de phase inconnu, renfort inconnu) ; `BadRule` (`chance` 0 ou 101, `belowHealthPercent` 0, `minRange` −1 ou au-delà de la portée de la compétence) ; `BadPhase` (`atHealthPercent` 0, 100, doublon ; `count` 0 ou 9 ; `level` 0 accepté, 21 refusé avec `MaxLevel` 20) ; `BossSummon` (renfort de rang `boss`) ; avertissement `NoSkills` pour un `elite` sans compétence. `SkillKindTests` : `classes: []` accepté ; `BadTelegraph` (`size` 0, 11 ; `width` 2, 7 ; `angle` 20, 190) ; `TelegraphWithoutCast` (sur `instant` et sur `channel`) ; `TelegraphNeedsAim` (`line` en mode `self`, `cone` en mode `around_caster`).

```csharp
[Fact]
public void ARuleTooFarForItsSkill_IsRefused()
{
    MonsterDefinition m = Wolf() with
    {
        Skills = [new MonsterSkillRule { Skill = "skill.monster.bite", MinRange = 4 }],
    };

    Check(m, Bite(range: 1)).Should().ContainSingle(f => f.Code == "BadRule" && f.Id == m.Id);
}
```

- [ ] **Step 2 :** `dotnet test Vortex.Rpg.Tests --filter "FullyQualifiedName~MonsterKind"` et `dotnet test Vortex.Rpg.Skills.Tests --filter "FullyQualifiedName~SkillKind"` → échec de compilation.
- [ ] **Step 3 : implémenter.** `SkillKind` : retirer le refus `NoClass` (garder `BrokenReference` sur chaque classe listée) ; ajouter :

```csharp
if (s.Telegraph is { } t)
{
    if (t.Size is < 1 or > 10 || t.Width is < 1 or > 5 || t.Width % 2 == 0 || t.Angle is < 30 or > 180)
    {
        yield return Error("BadTelegraph", s.Id, "Telegraph: size 1–10, odd width 1–5, angle 30–180.");
    }

    if (s.Type != SkillType.Cast)
    {
        yield return Error("TelegraphWithoutCast", s.Id, "A telegraph is the time of a cast: type cast with castMs.");
    }

    if (t.Shape != TelegraphShape.Circle && s.Target is SkillTarget.Self or SkillTarget.AroundCaster)
    {
        yield return Error("TelegraphNeedsAim", s.Id, "A line or a cone needs a target or a cell to aim at.");
    }
}
```

`MonsterKind` : clés `HashSet<string>` des comportements ; règles et phases validées comme Step 1 (`check.Get<SkillDefinition>` pour la portée, `check.Get<MonsterDefinition>` pour le rang du renfort, `check.Is<StatusDefinition>`, `MaxLevel` lu dans `check.All<ProgressionDefinition>()` comme le fait déjà `ClassKind`/`ProgressionKind` — à défaut 20). `MonstersModule` : chaque comportement enregistré deux fois sur la même instance :

```csharp
services.AddSingleton<MeleeBehavior>();
services.AddSingleton<IMonsterBehavior>(sp => sp.GetRequiredService<MeleeBehavior>());
services.AddSingleton<IMonsterBehaviorKey>(sp => sp.GetRequiredService<MeleeBehavior>());
```

`ContentSchemaTests` et `ContentPackInRepoTests` : `new MonsterKind([new BehaviorKey("melee"), new BehaviorKey("ranged"), new BehaviorKey("caster")])` avec un petit `record BehaviorKey(string Key) : IMonsterBehaviorKey` dans le dossier `Support` de Hosting.Tests (un fichier). Schémas : `UPDATE_CONTENT_SCHEMAS=1 dotnet test Vortex.Hosting.Tests -o Vortex.Hosting.Tests/bin/alt --filter ContentSchemaTests`.
- [ ] **Step 4 :** filtres du Step 2 + `dotnet test Vortex.Hosting.Tests -o Vortex.Hosting.Tests/bin/alt --filter "FullyQualifiedName~Content"` → verts (le contenu du dépôt passe toujours) ; `check-architecture-walls.mjs`, `check-code-layout.mjs` → 0.
- [ ] **Step 5 : commit** `monstres : contenu M1c — rang, règles d'usage, phases, télégraphe`.

---

### Task 2 : Le fil — 9025, 9026, `rank` dans 9009, `immune`

**Files :**
- Create : `Vortex.Protocol/Messages/Outgoing/Rpg/Skills/{RpgTelegraph,RpgTelegraphsMessageComposer}.cs`, `Vortex.Protocol/Messages/Outgoing/Rpg/Combat/RpgMonsterYellMessageComposer.cs`, `Vortex.Revisions/Revision20260701/Serializers/Rpg/Skills/RpgTelegraphsMessageComposerSerializer.cs`, `…/Serializers/Rpg/Combat/RpgMonsterYellMessageComposerSerializer.cs`
- Modify : `Vortex.Revisions/Revision20260701/Headers.cs`, `Maps/RpgSkillsMap.cs`, `Maps/RpgCombatMap.cs`, `Vortex.Protocol/Messages/Outgoing/Rpg/Interaction/RpgTargetMessageComposer.cs`, `…/Serializers/Rpg/Interaction/RpgTargetMessageComposerSerializer.cs`, `Vortex.Protocol/Messages/Outgoing/Rpg/Skills/RpgSkillOutcome.cs`
- Test : `Vortex.Revisions.Tests/Rpg/MonstersWireTests.cs` (nouveau), test existant du fil de `RpgTarget`

**Interfaces :**
- Produces :
  - `[GenerateSerializer, Immutable] sealed record RpgTelegraph { [Id(0)] required int CasterObjectId; [Id(1)] required string SkillId; [Id(2)] required string Shape; [Id(3)] required int DelayMs; [Id(4)] required ImmutableArray<(int X, int Y)> Cells; }` — si Orleans refuse le tuple, `ImmutableArray<int>` à plat (`x0, y0, x1, y1…`), même ordre sur le fil.
  - `RpgTelegraphsMessageComposer { required ImmutableArray<RpgTelegraph> Telegraphs }` (9025).
  - `RpgMonsterYellMessageComposer { required int ObjectId; required string TextKey; }` (9026).
  - `RpgTargetMessageComposer.Rank` (`string`, défaut `RankNormal = "normal"`), constantes `RankElite = "elite"`, `RankBoss = "boss"` ; `RpgSkillOutcome.Immune = "immune"`.

- [ ] **Step 1 : tests qui échouent.** `MonstersWireTests` (copie de `Vortex.Revisions.Tests/Room/UserChangeWireTests.cs`) : 9025 avec deux télégraphes (un cercle de 5 cases, une ligne de 3) relu dans l'ordre des Global Constraints ; 9026 relu ; 9009 d'un monstre élite se termine par `"elite"`, celui d'un joueur par `"normal"`.
- [ ] **Step 2 :** `dotnet test Vortex.Revisions.Tests --filter "FullyQualifiedName~MonstersWire|FullyQualifiedName~RpgTarget"` → échec de compilation.
- [ ] **Step 3 : implémenter.** Headers `RpgTelegraphsMessageComposer = 9025`, `RpgMonsterYellMessageComposer = 9026` (`// RPG (spec §3.7)`) ; sérialiseurs ; `RpgSkillsMap` enregistre 9025, `RpgCombatMap` 9026 ; `RpgTargetMessageComposerSerializer` écrit `Rank` après `ClassId`.
- [ ] **Step 4 :** filtres du Step 2 verts ; `dotnet test Vortex.Revisions.Tests` vert (`SerializerPairingTests`) ; `node scripts/hooks/check-inert-declarations.mjs` → les deux nouveaux composers sont signalés tant que rien ne les émet : noter le compte, il retombe à 0 aux Tâches 5 et 9.
- [ ] **Step 5 : commit** `monstres : paquets télégraphes (9025), cri (9026), rang de la cible`.

---

### Task 3 : `TelegraphCells` — les cases de chaque forme

**Files :**
- Create : `Vortex.Rpg.Combat/Telegraphs/TelegraphCells.cs`
- Test : `Vortex.Rpg.Combat.Tests/Telegraphs/TelegraphCellsTests.cs`

**Interfaces :**
- Consumes : `SkillTelegraph`, `TelegraphShape` (Tâche 1).
- Produces : `public static class TelegraphCells { public static IReadOnlyList<(int X, int Y)> Of(SkillTelegraph t, (int X, int Y) origin, (int X, int Y) toward, Func<int, int, bool> walkable); }` — cercle centré sur `origin` ; ligne et cône partent de `origin` vers `toward` ; cases marchables seulement, sans doublon, triées par distance à `origin` puis `Y` puis `X`.

- [ ] **Step 1 : tests qui échouent.** Cercle `size` 1 → 5 cases (croix), `size` 2 → 13 ; ligne `size` 3, `width` 1, vers l'est → (1,0),(2,0),(3,0) depuis (0,0) ; vers le nord-est → diagonale ; `width` 3 vers l'est → 9 cases ; cône `size` 2, 90° vers l'est → cases d'angle ≤ 45° ; 180° → demi-disque ; les 8 directions donnent le même nombre de cases pour un cône 90° de taille 3 ; une case non marchable est retirée ; `origin == toward` → direction est (`ponytail`).

```csharp
[Fact]
public void AnEastLine_OfWidthThree_IsThreeRowsOfThree()
{
    TelegraphCells.Of(Line(size: 3, width: 3), (0, 0), (5, 0), (_, _) => true)
        .Should().BeEquivalentTo(new[] { (1, -1), (1, 0), (1, 1), (2, -1), (2, 0), (2, 1), (3, -1), (3, 0), (3, 1) });
}
```

- [ ] **Step 2 :** `dotnet test Vortex.Rpg.Combat.Tests --filter "FullyQualifiedName~TelegraphCells"` → échec de compilation.
- [ ] **Step 3 : implémenter.**

```csharp
public static IReadOnlyList<(int X, int Y)> Of(
    SkillTelegraph t, (int X, int Y) origin, (int X, int Y) toward, Func<int, int, bool> walkable)
{
    double angle = toward == origin ? 0 : Math.Atan2(toward.Y - origin.Y, toward.X - origin.X); // ponytail: no aim → east
    IEnumerable<(int X, int Y)> cells = t.Shape switch
    {
        TelegraphShape.Circle => Square(origin, t.Size).Where(c => Sq(c, origin) <= t.Size * t.Size),
        TelegraphShape.Line => Line(origin, angle, t.Size, t.Width),
        _ => Square(origin, t.Size).Where(c =>
        {
            double d = Math.Sqrt(Sq(c, origin));
            double off = Math.Abs(Math.IEEERemainder(Math.Atan2(c.Y - origin.Y, c.X - origin.X) - angle, 2 * Math.PI));
            return d >= 1 && d <= t.Size && off <= t.Angle * Math.PI / 360 + 1e-9;
        }),
    };

    return [.. cells.Where(c => walkable(c.X, c.Y)).Distinct().OrderBy(c => Sq(c, origin)).ThenBy(c => c.Y).ThenBy(c => c.X)];
}

private static IEnumerable<(int X, int Y)> Line((int X, int Y) o, double angle, int size, int width)
{
    int octant = (int)Math.Round(angle / (Math.PI / 4));
    (int dx, int dy) = (Math.Sign(Math.Round(Math.Cos(octant * Math.PI / 4), 6)), Math.Sign(Math.Round(Math.Sin(octant * Math.PI / 4), 6)));
    int half = (width - 1) / 2;

    for (int step = 1; step <= size; step++)
    {
        for (int w = -half; w <= half; w++)
        {
            yield return (o.X + dx * step - dy * w, o.Y + dy * step + dx * w);
        }
    }
}
```

(`Square` énumère le carré `±size` autour de `origin` ; `Sq` = distance euclidienne au carré.)
- [ ] **Step 4 :** filtre du Step 2 vert.
- [ ] **Step 5 : commit** `combat : cases des télégraphes (cercle, ligne, cône)`.

---

### Task 4 : Une machine d'incantation pour tout lanceur

**Files :**
- Modify : `Vortex.Rpg.Combat/Systems/{CastRequest,ICastingSystem}.cs`, `Vortex.Rpg.Combat/Zones/CombatZone.Extensions.cs`, `Vortex.Rpg.Monsters/Zones/RpgZoneSimulation.cs`, `Vortex.Rpg.Skills/Casting/CastSystem.cs`, `Vortex.Rpg.Skills/Targeting/{SkillTargets,TargetCheck}.cs`, `Vortex.Rpg.Skills/Effects/*` (appels à `IsAlly`/`IsEnemy`), `Vortex.Rpg.Skills.Tests/Support/SkillWorld.cs` (`Cast` traduit le joueur)
- Test : `Vortex.Rpg.Skills.Tests/MonsterCastTests.cs`

**Interfaces :**
- Produces :
  - `public sealed record CastRequest(int CasterObjectId, string SkillId, int TargetObjectId, int X, int Y);`
  - `ICastingSystem.Reset(CombatZone zone, Combatant c)` : incantation annulée (`cancelled`), approche oubliée, recharges et délai global vidés. `CombatZone.ResetCasts(Combatant c)`.
  - `SkillTargets.IsAlly(Combatant caster, Combatant c)` = même camp (`c.IsPlayer == caster.IsPlayer`), vivant, pas fantôme, et pour un monstre pas invulnérable ; `SkillTargets.IsEnemy(Combatant caster, Combatant c)` = autre camp, vivant, pas fantôme, pas invulnérable.

- [ ] **Step 1 : tests qui échouent.** `MonsterCastTests` (`SkillWorld.WithCasting` ; un loup ajouté par `Combat.AddMonster`, sa compétence lancée par `Combat.Cast(new CastRequest(wolf.ObjectId, …))`) : un monstre lance une compétence `classes: []` sans mana (`Mana` 0) ; le joueur prend les dégâts ; hors portée → `unreachable` sans marcher (`Zone.Walks` vide) ; sans ligne de vue → `no_line_of_sight` ; recharge et délai global respectés ; un étourdissement interrompt ; un coup reçu n'interrompt pas ; `ResetCasts` annule et vide les recharges ; mort du monstre → `RpgCastEnd(cancelled)` ; aucun refus de monstre compté dans `SkillCast` (Décision 2), la fin `resolved` l'est ; un soin `ally` d'un monstre soigne un monstre, jamais un joueur ; un sort de joueur `ally` ne soigne jamais un monstre (régression).

```csharp
[Fact]
public async Task AMonster_CastsWithoutMana_AndHitsThePlayer()
{
    SkillWorld w = SkillWorld.WithCasting(Spit());
    Combatant me = await w.PlayerAsync(x: 5, y: 5);
    Combatant wolf = w.Wolf(x: 7, y: 5);

    w.Combat.Cast(new CastRequest(wolf.ObjectId, Spit().Id, me.ObjectId, -1, -1)).Should().BeNull();
    await w.RunAsync(Spit().CastMs + 250);

    me.Health.Should().BeLessThan(me.MaxHealth);
    wolf.Mana.Should().Be(0);
}
```

- [ ] **Step 2 :** `dotnet test Vortex.Rpg.Skills.Tests --filter "FullyQualifiedName~MonsterCast"` → échec de compilation.
- [ ] **Step 3 : implémenter.** `RpgZoneSimulation.CastSkillAsync` : `combat.PlayerCombatant(player) is { } me ? combat.Cast(new CastRequest(me.ObjectId, skillId, targetObjectId, x, y)) : RpgSkillResultMessageComposer.Dead`. `CastSystem` :
  - `Accept` : `zone.Combatants.TryGetValue(request.CasterObjectId, out Combatant? caster)` ; `Knows` seulement si `caster.IsPlayer` ;
  - hors portée : un joueur approche comme avant ; un monstre est refusé `InRange(...) ? NoLineOfSight : Unreachable` ;
  - `Begin`, fin d'incantation et `Pay` : la mana seulement pour un joueur ;
  - `Cast` : `metrics.SkillCast` sur refus seulement si le lanceur est un joueur ;
  - `Tick` : `foreach (Combatant c in zone.Combatants.Values.ToArray())` ;
  - `Reset` : `End(zone, c, RpgCastEnd.Cancelled); c.Set<PendingCast>(null); c.Set<SkillCooldowns>(null); Steer(c);`.

`TargetCheck` et `SkillTargets.Area` passent le lanceur à `IsAlly`/`IsEnemy`. Contrôle de case sûre inchangé (un monstre ne vise jamais un joueur sur case sûre).
- [ ] **Step 4 :** `dotnet test Vortex.Rpg.Skills.Tests` (tout : `CastTests`, `TargetingTests`, `EffectTests` passent toujours), `dotnet test Vortex.Rpg.Monsters.Tests`, `dotnet test Vortex.Rpg.Combat.Tests` → verts.
- [ ] **Step 5 : commit** `compétences : une machine d'incantation pour joueurs et monstres`.

---

### Task 5 : Les télégraphes dans l'incantation

**Files :**
- Create : `Vortex.Rpg.Skills/Casting/TelegraphNews.cs`
- Modify : `Vortex.Rpg.Skills/Casting/{CastState,CastSystem}.cs`, `Vortex.Rpg.Skills/Targeting/SkillTargets.cs`
- Test : `Vortex.Rpg.Skills.Tests/TelegraphTests.cs`

**Interfaces :**
- Consumes : `TelegraphCells.Of` (Tâche 3), `RpgTelegraph`, `RpgTelegraphsMessageComposer` (Tâche 2), `CastRequest` (Tâche 4).
- Produces : `CastState.Cells` (`IReadOnlyList<(int X, int Y)>?`), `CastState.Origin` (`(int X, int Y)`) ; `TelegraphNews` (partie de zone, `Add`, `Clear`, `Flush`) ; `SkillTargets.InCells(SkillContext ctx, IReadOnlyList<(int X, int Y)> cells, (int X, int Y) origin)`.

- [ ] **Step 1 : tests qui échouent.** `TelegraphTests` (un monstre lance `skill.test.cone` : cône 3, 90°, `castMs` 1500) :
  - 9025 diffusé une fois au début, cases = `TelegraphCells.Of(...)` ;
  - le joueur qui sort des cases avant l'impact n'est pas touché ; un joueur qui y entre l'est ;
  - `APlayerOnASafeCell_IsNotHitByATelegraph` (Review Focus 2) ;
  - `maxTargets` 1 → le plus proche de l'origine ;
  - la cible initiale morte ou partie : l'incantation continue (pas de contrôle de cible unique pour un télégraphe) ;
  - `ACasterDyingMidTelegraph_LandsNothing` (Review Focus 1) : `RpgCastEnd(cancelled)`, aucun impact ;
  - un joueur entré dans la zone pendant le télégraphe reçoit 9025 dans `Entry` avec le délai restant ;
  - le premier joueur d'une zone vide n'entend pas les télégraphes passés (comme `CastNews`).
- [ ] **Step 2 :** `dotnet test Vortex.Rpg.Skills.Tests --filter "FullyQualifiedName~Telegraph"` → échec.
- [ ] **Step 3 : implémenter.** Dans `Start`, si `pending.Skill.Telegraph is { } t` :

```csharp
(int X, int Y) me = zone.Runtime.PositionOf(c.ObjectId) ?? (0, 0);
(int X, int Y) aim = pending.Cell ?? zone.Runtime.PositionOf(pending.TargetObjectId) ?? me;
(int X, int Y) origin = t.Shape == TelegraphShape.Circle
    && pending.Skill.Target is not (SkillTarget.Self or SkillTarget.AroundCaster) ? aim : me;
IReadOnlyList<(int X, int Y)> cells = TelegraphCells.Of(t, origin, aim, zone.Runtime.IsWalkable);
```

`CastState` reçoit `Cells` et `Origin` ; `TelegraphNews.Add(new RpgTelegraph { …, Shape = t.Shape.ToString().ToLowerInvariant(), DelayMs = pending.Skill.CastMs, Cells = [.. cells] })`. `TickCast` : pour un télégraphe, pas de contrôle de cible unique ni de portée à la fin. `Land` : `state.Cells is { } cells ? SkillTargets.InCells(ctx, cells, state.Origin) : SkillTargets.Resolve(...)` ; `InCells` = combattants du bon camp (`IsEnemy`/`IsAlly` selon `IsOffensive`) dont la position est dans `cells` et **pas sur une case sûre** pour un sort offensif, triés par distance euclidienne à `origin` puis id, `Take(MaxTargets)`. `BeforeFlush` vide aussi `TelegraphNews` ; `Added` (premier joueur) le vide ; `Entry` renvoie un 9025 des `CastState` à `Cells` non nul, `DelayMs = EndsAtMs − now`.
- [ ] **Step 4 :** `dotnet test Vortex.Rpg.Skills.Tests` vert ; `check-inert-declarations.mjs` : 9025 n'est plus signalé.
- [ ] **Step 5 : commit** `compétences : télégraphes — cases figées, impact par cases, état d'entrée`.

---

### Task 6 : Statuts posés par un autre module, statuts sans fin, immunités

**Files :**
- Create : `Vortex.Rpg.Combat/Systems/IStatusSystem.cs`, `Vortex.Rpg.Combat/Combatants/RankScale.cs`
- Modify : `Vortex.Rpg.Combat/Combatants/Combatant.cs` (`Immunities`), `Vortex.Rpg.Combat/Zones/CombatZone.cs` (`AddMonster(…, RankScale? rank = null)`), `CombatZone.Extensions.cs` (`ApplyStatus`, `RemoveStatus`), `Vortex.Rpg.Skills/Statuses/{StatusApplier,StatusSystem,ActiveStatus}.cs`, `Vortex.Rpg.Skills/Effects/ApplyStatusEffect.cs`, `Vortex.Rpg.Skills/SkillsModule.cs` (forward `IStatusSystem`)
- Test : `Vortex.Rpg.Skills.Tests/StatusFromModuleTests.cs`, `Vortex.Rpg.Combat.Tests/Zones/RankScaleTests.cs`

**Interfaces :**
- Produces :
  - `public interface IStatusSystem : ICombatSystem { void Apply(CombatZone zone, Combatant source, Combatant target, string statusId, bool permanent); void Remove(CombatZone zone, Combatant target, string statusId); }` ; `CombatZone.ApplyStatus(Combatant source, Combatant target, string statusId, bool permanent)`, `CombatZone.RemoveStatus(Combatant target, string statusId)` (le dernier `IStatusSystem` déclaré ; aucun : rien).
  - `public sealed record RankScale(double Health, double Damage, double Xp, IReadOnlySet<StatusType> Immunities) { public static readonly RankScale None = new(1, 1, 1, FrozenSet<StatusType>.Empty); }`
  - `Combatant.Immunities` (`IReadOnlySet<StatusType>`, défaut vide).
  - `ActiveStatus.Permanent` ; `StatusApplier.Apply(…, bool permanent = false)` ; `StatusApplier.IsImmune(Combatant target, StatusDefinition status)`.

- [ ] **Step 1 : tests qui échouent.**
  - `RankScaleTests` : `AddMonster` avec `new RankScale(3, 1.5, 3, …)` → PV, dégâts min/max, XP multipliés (arrondi au plus proche, PV ≥ 1), immunités recopiées ; sans rang → inchangé.
  - `StatusFromModuleTests` : `Combat.ApplyStatus(boss, boss, "status.enraged", permanent: true)` → le statut ne finit jamais (`RunAsync(600000)`), `RpgStatuses` envoie `RemainingMs = -1`, les stats sont recalculées ; `RemoveStatus` l'enlève ; un statut d'un type immunisé (`stun` sur un boss) n'est pas posé et `RpgSkillEvents` porte `immune` ; l'effet `interrupt` interrompt quand même un boss immunisé.
- [ ] **Step 2 :** `dotnet test Vortex.Rpg.Combat.Tests --filter "FullyQualifiedName~RankScale"` ; `dotnet test Vortex.Rpg.Skills.Tests --filter "FullyQualifiedName~StatusFromModule"` → échec.
- [ ] **Step 3 : implémenter.** `AddMonster` : `RankScale r = rank ?? RankScale.None;` puis `MaxHealth = Math.Max(1, (int)Math.Round((b.Health + l.Health * above) * r.Health))`, idem dégâts (`r.Damage`) et `Xp` (`r.Xp`), `Immunities = r.Immunities`. `StatusApplier.Apply` commence par `if (IsImmune(target, status)) return false;` ; `Put` reçoit `permanent` → `EndsAtMs = long.MaxValue`, `Permanent = true` ; l'expiration de `StatusSystem` saute les permanents ; les deux calculs de `RemainingMs` (lignes ≈ 133 et ≈ 313) donnent `-1` pour un permanent. `ApplyStatusEffect` : `if (StatusApplier.IsImmune(target, status)) { ctx.Report(target, 0, 0, RpgSkillOutcome.Immune); return; }`. `StatusSystem : IStatusSystem` : `Apply` lit la définition dans le contenu (inconnue : rien), `Remove` retire l'actif de cet id et recalcule. `SkillsModule` : `services.AddSingleton<IStatusSystem>(sp => sp.GetRequiredService<StatusSystem>())` sur l'instance déjà déclarée.
- [ ] **Step 4 :** `dotnet test Vortex.Rpg.Skills.Tests`, `dotnet test Vortex.Rpg.Combat.Tests` verts.
- [ ] **Step 5 : commit** `compétences : statuts posés par un module, sans fin, immunités de rang`.

---

### Task 7 : Base des comportements, rang à l'apparition, sélecteur de compétence

**Files :**
- Create : `Vortex.Rpg.Monsters/Behaviors/MonsterBehavior.cs`, `Vortex.Rpg.Monsters/Skills/MonsterSkillSelector.cs`, `Vortex.Rpg.Monsters/Configuration/{MonstersConfig,MonsterRankConfig}.cs`
- Modify : `Vortex.Rpg.Monsters/Behaviors/{MeleeBehavior,MonsterBrain}.cs`, `Vortex.Rpg.Monsters/Spawns/{LiveMonster,MonsterSpawner}.cs`, `Vortex.Rpg.Monsters/Zones/{RpgZoneSimulation,RpgZoneSimulationFactory}.cs`, `Vortex.Rpg.Monsters/MonstersModule.cs`, `Vortex.Rpg.Monsters.Tests/Support/MonsterWorld.cs`
- Test : `Vortex.Rpg.Monsters.Tests/MonsterSkillSelectorTests.cs`, `MonstersConfigTests.cs`, `MonsterRankTests.cs`

**Interfaces :**
- Consumes : `CastRequest` et `CombatZone.Cast` (Tâche 4), `RankScale`, `AddMonster(…, RankScale?)` (Tâche 6), `MonsterSkillRule`, `MonsterRank` (Tâche 1), `RpgTargetMessageComposer.Rank` (Tâche 2).
- Produces :
  - `MonstersConfig` (`Vortex:Monsters`) : `Dictionary<string, MonsterRankConfig> Ranks` (défauts `elite`, `boss` des Global Constraints), `int MaxSummonsPerMonster = 16`, `int RangedAimMs = 250`, `RankScale ScaleFor(MonsterRank rank)`, `IReadOnlyList<string> Problems()`. `MonsterRankConfig { double HealthMultiplier = 1; double DamageMultiplier = 1; double XpMultiplier = 1; List<string> Immunities = []; }`.
  - `MonsterBrain` gagne `required MonsterSpawner Spawner`, `required IRpgContent Content`, `required IVortexMetrics Metrics`, `required MonstersConfig Config`.
  - `LiveMonster` gagne `IReadOnlyList<MonsterSkillRule> Skills` (= `Definition.Skills` à l'apparition), `int Phase`, `string? PhaseStatus`, `int? Parent`, `List<int> Summons`, `long AimUntilMs`.
  - `public abstract class MonsterBehavior : IMonsterBehavior` — `Think` (le socle de `MeleeBehavior`), `protected virtual int DesiredRange(MonsterBrain brain)` (défaut `brain.Combatant.Range`), `protected virtual void Act(MonsterBrain brain, int targetObjectId, bool justStopped)` (défaut : `MonsterSkillSelector.TryCast`), `protected static void GoHome(MonsterBrain brain)` qui appelle la remise à zéro (Tâche 9 la complète).
  - `public static class MonsterSkillSelector { public static bool TryCast(MonsterBrain brain, int enemyObjectId); }` — vrai quand une incantation a démarré.

- [ ] **Step 1 : tests qui échouent.** `MonsterSkillSelectorTests` (un faux `ICastingSystem` dans la zone qui enregistre les requêtes et refuse selon une table) : priorité décroissante, ordre du fichier à égalité ; une règle refusée (`cooldown`) passe à la suivante ; `minRange` ; `belowHealthPercent` sur les PV du lanceur ; `chance` (`FixedRandom`) ; compétence retirée sautée ; cible par mode (spec §6.2) ; `AnAllySkill_PicksTheMostWoundedLiveMonster` (Review Focus 4 : pas un mort, pas un invulnérable, pas un joueur, lui-même s'il est le plus blessé) ; rien ne passe → aucune requête, le tir de base garde sa cible. `MonsterRankTests` : un élite apparaît avec PV ×3 ; un boss reçoit les immunités ; 9009 d'un boss porte `boss`. `MonstersConfigTests` : multiplicateur ≤ 0 et immunité inconnue (`"root"`) dans `Problems()`, avec le nom de la clé. Les tests existants (`MeleeBehaviorTests`, `StunnedMonsterTests`, `MonsterSpawnerTests`) passent inchangés.
- [ ] **Step 2 :** `dotnet test Vortex.Rpg.Monsters.Tests` → échec de compilation.
- [ ] **Step 3 : implémenter.** `MonsterBehavior.Think` = le corps actuel de `MeleeBehavior.Think`, avec : `if (c.Busy) return;` juste après le contrôle de laisse ; `DesiredRange(brain)` à la place de `c.Range` (comparaison et `WalkNear`) ; à portée :

```csharp
bool justStopped = zone.IsWalking(m.ObjectId);
m.State = MonsterState.Fight;
m.ChaseGoal = null;
zone.StopWalking(m.ObjectId);
Act(brain, top, justStopped);
```

`MeleeBehavior : MonsterBehavior` (clé `melee`, rien d'autre). Sélecteur :

```csharp
foreach (MonsterSkillRule rule in m.Skills.OrderByDescending(r => r.Priority))
{
    if (brain.Content.Get<SkillDefinition>(rule.Skill) is not { Retired: false } skill
        || Aim(brain, skill, rule, enemyObjectId) is not { } aim
        || aim.Distance < rule.MinRange
        || rule.Chance < 100 && brain.Runtime.Random.Next(0, 100) >= rule.Chance)
    {
        continue;
    }

    if (brain.Combat.Cast(new CastRequest(c.ObjectId, skill.Id, aim.ObjectId, aim.X, aim.Y)) is null)
    {
        return true;
    }
}

return false;
```

(`OrderByDescending` est stable : l'ordre du fichier départage. `Aim` rend l'objet visé, la case pour `ground`, la distance, et applique `belowHealthPercent` aux PV du lanceur, ou de l'allié choisi pour `ally`.) `MonsterSpawner` reçoit `MonstersConfig` et passe `config.ScaleFor(definition.Rank)` à `AddMonster` ; `LiveMonster.Skills = definition.Skills`. `RpgZoneSimulationFactory` reçoit `IOptions<MonstersConfig>` et `IVortexMetrics` (déjà là) ; `RpgZoneSimulation` construit le `MonsterBrain` complet et met `Rank` dans 9009 (`spawner.Live[objectId].Definition.Rank` en snake_case). `MonstersModule` :

```csharp
services.AddOptions<MonstersConfig>()
    .Bind(builder.Configuration.GetSection("Vortex:Monsters"))
    .Validate(c => c.Problems().Count == 0, "Vortex:Monsters is invalid (see the log)")
    .ValidateOnStart();
```

(et `Problems()` journalisé au démarrage par la fabrique si non vide.)
- [ ] **Step 4 :** `dotnet test Vortex.Rpg.Monsters.Tests` vert ; `dotnet test Vortex.Hosting.Tests -o Vortex.Hosting.Tests/bin/alt` vert (démarrage avec la configuration par défaut).
- [ ] **Step 5 : commit** `monstres : base des comportements, rang à l'apparition, sélecteur de compétence`.

---

### Task 8 : `ranged` et `caster`

**Files :**
- Create : `Vortex.Rpg.Monsters/Behaviors/{RangedBehavior,CasterBehavior}.cs`
- Modify : `Vortex.Rpg.Monsters/MonstersModule.cs`
- Test : `Vortex.Rpg.Monsters.Tests/RangedBehaviorTests.cs`, `CasterBehaviorTests.cs`

**Interfaces :**
- Consumes : `MonsterBehavior`, `MonsterSkillSelector`, `MonstersConfig.RangedAimMs`, `LiveMonster.AimUntilMs` (Tâche 7).
- Produces : `RangedBehavior` (clé `ranged`, `IOptions<MonstersConfig>`), `CasterBehavior` (clé `caster`), déclarés comme `melee` (deux enregistrements sur la même instance, Tâche 1).

- [ ] **Step 1 : tests qui échouent.** `RangedBehaviorTests` : un archer (`attackRange` 5) s'arrête à 5 cases avec ligne de vue et ne tire pas avant `RangedAimMs` (`NextSwingAtMs ≥ now + 250`) ; sans ligne de vue, il continue d'approcher ; déjà immobile, il tire à sa cadence ; il n'utilise pas le sélecteur pendant la visée. `CasterBehaviorTests` : un chaman dont la compétence offensive a une portée 4 s'arrête à 4 cases (pas à son `attackRange` 1) ; sans compétence disponible il s'approche à `attackRange` ; il lance d'abord, le tir de base ensuite.
- [ ] **Step 2 :** `dotnet test Vortex.Rpg.Monsters.Tests --filter "FullyQualifiedName~Ranged|FullyQualifiedName~Caster"` → échec.
- [ ] **Step 3 : implémenter.**

```csharp
protected override void Act(MonsterBrain brain, int targetObjectId, bool justStopped)
{
    long now = brain.Runtime.NowMs;

    if (justStopped)
    {
        brain.Monster.AimUntilMs = now + _config.RangedAimMs;
        brain.Combatant.NextSwingAtMs = Math.Max(brain.Combatant.NextSwingAtMs, brain.Monster.AimUntilMs);

        return;
    }

    if (now >= brain.Monster.AimUntilMs)
    {
        MonsterSkillSelector.TryCast(brain, targetObjectId);
    }
}
```

`CasterBehavior.DesiredRange` (Décision 5) :

```csharp
int[] ranges = [.. brain.Monster.Skills
    .Select(r => brain.Content.Get<SkillDefinition>(r.Skill))
    .Where(s => s is { Retired: false, Target: SkillTarget.Enemy or SkillTarget.Ground or SkillTarget.AroundTarget })
    .Select(s => s!.Range)];

return ranges.Length > 0 ? Math.Max(1, ranges.Min()) : brain.Combatant.Range;
```

- [ ] **Step 4 :** `dotnet test Vortex.Rpg.Monsters.Tests` vert.
- [ ] **Step 5 : commit** `monstres : comportements ranged et caster`.

---

### Task 9 : Phases, renforts, cri, remise à zéro

**Files :**
- Create : `Vortex.Rpg.Monsters/Phases/BossPhases.cs`
- Modify : `Vortex.Rpg.Monsters/Behaviors/MonsterBehavior.cs` (`GoHome` → `Reset`), `Vortex.Rpg.Monsters/Spawns/MonsterSpawner.cs` (`Summon`, `Dismiss`, `Died` d'un renfort), `Vortex.Rpg.Monsters/Zones/RpgZoneSimulation.cs` (phases avant la pensée), `Vortex.Rpg.Combat/Combatants/ThreatTable.cs` (`Entries`), `Vortex.Primitives/Observability/IVortexMetrics.cs`, `Vortex.Observability/Metrics/VortexMetrics.cs`, faux de métriques des tests (grep `IVortexMetrics`)
- Test : `Vortex.Rpg.Monsters.Tests/BossPhasesTests.cs`, `SummonTests.cs`, `MonsterResetTests.cs`

**Interfaces :**
- Consumes : `CombatZone.ApplyStatus`, `RemoveStatus` (Tâche 6), `ResetCasts` (Tâche 4), `RpgMonsterYellMessageComposer` (Tâche 2), `MonsterBrain` complet (Tâche 7).
- Produces :
  - `public static class BossPhases { public static void Check(MonsterBrain brain); public static void Reset(MonsterBrain brain); }`
  - `MonsterSpawner.Summon(LiveMonster parent, MonsterDefinition definition, int count, int level, long now)`, `MonsterSpawner.Dismiss(LiveMonster parent)` ; le spawner reçoit le dictionnaire des comportements par clé.
  - `ThreatTable.Entries` (`IReadOnlyList<(int ObjectId, long Amount)>`, premier en tête).
  - `IVortexMetrics.BossPhase(string monsterId, int phase)`, `IVortexMetrics.BossReset(string monsterId)`.

- [ ] **Step 1 : tests qui échouent.**
  - `BossPhasesTests` : PV sous 66 % → phase jouée une fois (cri 9026 diffusé, statut permanent posé, liste remplacée, métrique) ; une liste vide garde la précédente ; deux seuils d'un coup (70 % → 30 %) jouent les deux dans l'ordre, la dernière liste gagne, le statut de la phase 1 est retiré au profit de celui de la phase 2.
  - `SummonTests` : 2 loups sur des cases libres non sûres à 1–2 cases ; menace du boss recopiée, dans l'ordre ; niveau de la phase ou du boss ; plafond (`MaxSummonsPerMonster` 2 → la 2ᵉ invocation de 2 est ignorée, avertissement) ; aucune case → aucun renfort, avertissement ; `AKilledSummon_NeverRespawns_AndFreesItsSlot` (Review Focus 5) ; `TheBossDying_LeavesItsSummons` (Review Focus 1).
  - `MonsterResetTests` : laisse franchie en phase 2 → PV pleins, statut de phase retiré, liste d'origine, renforts disparus **sans XP** (aucun `Participants` crédité), recharges vidées, incantation annulée, métrique `BossReset` ; `AReset_LetsThePhasesPlayAgain` (Review Focus 3) ; un monstre sans phases rentre comme en M1a (régression `MeleeBehaviorTests`).
- [ ] **Step 2 :** `dotnet test Vortex.Rpg.Monsters.Tests --filter "FullyQualifiedName~BossPhases|FullyQualifiedName~Summon|FullyQualifiedName~MonsterReset"` → échec.
- [ ] **Step 3 : implémenter.**

```csharp
public static void Check(MonsterBrain brain)
{
    LiveMonster m = brain.Monster;
    Combatant c = brain.Combatant;
    MonsterPhase[] phases = [.. m.Definition.Phases.OrderByDescending(p => p.AtHealthPercent)];

    while (!c.IsDead && m.Phase < phases.Length && 100.0 * c.Health / c.MaxHealth < phases[m.Phase].AtHealthPercent)
    {
        Enter(brain, phases[m.Phase], ++m.Phase);
    }
}

private static void Enter(MonsterBrain brain, MonsterPhase phase, int index)
{
    LiveMonster m = brain.Monster;
    Combatant c = brain.Combatant;

    if (phase.Skills.Count > 0)
    {
        m.Skills = phase.Skills;
    }

    if (m.PhaseStatus is { } old)
    {
        brain.Combat.RemoveStatus(c, old);
    }

    m.PhaseStatus = phase.Status;

    if (phase.Status is { } status)
    {
        brain.Combat.ApplyStatus(c, c, status, permanent: true);
    }

    if (phase.Summon is { } s && brain.Content.Get<MonsterDefinition>(s.Monster) is { Retired: false } def)
    {
        brain.Spawner.Summon(m, def, s.Count, s.Level > 0 ? s.Level : c.Level, brain.Runtime.NowMs);
    }

    if (phase.Yell is { } yell)
    {
        brain.Combat.Broadcast(new RpgMonsterYellMessageComposer { ObjectId = m.ObjectId, TextKey = yell });
    }

    brain.Metrics.BossPhase(m.Definition.Id, index);
}
```

`Reset` : `ResetCasts(c)`, statut de phase retiré, `Phase = 0`, `Skills = Definition.Skills`, `Spawner.Dismiss(m)`, `BossReset` si le monstre a des phases. `MonsterBehavior.GoHome` appelle `BossPhases.Reset(brain)` avant le retour (pour tout monstre : recharges et incantation). `RpgZoneSimulation.TickAsync` appelle `BossPhases.Check` pour chaque monstre vivant avant `Think`. `Summon` : cellules libres non sûres à Chebyshev 1 puis 2 autour du parent (ordre mélangé par `zone.Random` comme `Spawn`), `zone.SpawnMonster` + `combat.AddMonster(id, level, def, config.ScaleFor(def.Rank))`, `LiveMonster { Spawn = new SpawnPoint { Source = parent.Spawn.Source, Definition = def, Behavior = behaviors[def.Behavior] }, Home = cell, Parent = parent.ObjectId }`, menace recopiée depuis `Entries`, `parent.Summons.Add(id)`. `Dismiss` : `combat.RemoveMonster(id)` et `_live.Remove(id)` pour chaque renfort. `Died` : un renfort ne réenfile rien et se retire de `Summons` de son parent s'il vit encore.
- [ ] **Step 4 :** `dotnet test Vortex.Rpg.Monsters.Tests` vert ; `check-inert-declarations.mjs` → 0.
- [ ] **Step 5 : commit** `monstres : phases de boss, renforts, cri, remise à zéro complète`.

---

### Task 10 : Contenu de démonstration, preuve `boss`, documentation, porte, contrat client

**Files :**
- Create : `content/monster/{goblin_archer,goblin_shaman,alpha_wolf,grove_guardian}.json`, `content/skill/{monster_sweep,monster_root_lash,monster_quake,monster_mend,monster_frost_bolt,monster_howl}.json`, `content/status/{howling,enraged}.json`
- Modify : `content/texts/fr.txt`, `.claude/skills/run-vortex-emulator/driver.mjs` (`cmdBoss`, `publishBossZone`), `.claude/skills/run-vortex-emulator/SKILL.md`, `docs/architecture.md`, `AGENTS.md` (ligne « monster behaviour » : `ranged`, `caster`, phases), `docs/superpowers/specs/2026-10-10-rpg-monsters-m1c-design.md` (statut livré, Décision 4 reportée en §8.1), `Vortex.Rpg.Skills.Tests/StartingSkillsTests.cs` ou `RepositoryContent` (le contenu du dépôt charge)
- Test : `Vortex.Hosting.Tests/Rpg/ContentPackInRepoTests.cs` (le pack du dépôt passe avec les nouveaux fichiers)

- [ ] **Step 1 : contenu.** Ids et valeurs de spec §15 : `skill.monster.sweep` (cône 3, 90°, `cast` 1500, `enemy`, portée 2, dégâts coefficient 1.2), `skill.monster.root_lash` (ligne 5 largeur 1, `cast` 1500, `enemy`, portée 5, dégâts 1.0 + `status.frozen`), `skill.monster.quake` (cercle 3, `cast` 2000, `around_target`, rayon 3, portée 6, dégâts 1.5), `skill.monster.mend` (`ally`, instantané, portée 5, soin 1.0, cooldown 8000), `skill.monster.frost_bolt` (`enemy`, `cast` 1500, portée 4, dégâts 0.6 + `status.frozen`), `skill.monster.howl` (`self`, instantané, cooldown 20000, `apply_status` `status.howling`) ; toutes `"classes": []`, `"level": 1`. `status.howling` (buff, 8000, refresh, `damage_dealt_percent` +20), `status.enraged` (buff, 1000 — la durée est ignorée quand la phase le pose sans fin —, refresh, `damage_dealt_percent` +30). Monstres : archer `ranged` portée 5 ; chaman `caster` règles `mend` (`belowHealthPercent` 50, priorité 20) et `frost_bolt` (priorité 10) ; loup alpha `elite` règle `howl` ; gardien du bosquet `boss`, phases 66 % et 33 % comme spec §15 ; stats réglées pour qu'un mage de niveau 6 amène le boss niveau 1 sous 66 % sans mourir. Textes : noms des monstres, compétences, statuts, cris `monster.grove.guardian.phase2`, `monster.grove.guardian.phase3`.
- [ ] **Step 2 :** `dotnet test Vortex.Hosting.Tests -o Vortex.Hosting.Tests/bin/alt --filter "FullyQualifiedName~Content"` et `dotnet test Vortex.Rpg.Skills.Tests` → verts.
- [ ] **Step 3 : driver.** `cmdBoss` (copie de `cmdSkills` et de `publishCombatZones`) :
  1. crédite d'XP le mage de preuve (`run-skill-c@`) par `POST /api/admin/characters/{id}/xp` jusqu'au niveau 6 ;
  2. publie `proof_boss` (12×12, camp sûr à l'arrivée (1, 6), boss en (8, 6) `aggroRadius` 3, archer et chaman en (11, 0) et (11, 11) `aggroRadius` 1) et y entre ;
  3. s'approche ; attend `RpgTelegraphs` (9025) du boss, sort des cases, attend `RpgSkillEvents` (9024) et vérifie qu'il n'y figure pas comme cible touchée ;
  4. frappe (attaque et Trait de feu) jusqu'à 66 % : attend `RpgMonsterYell` (9026), `RpgStatuses` avec `status.enraged` sur le boss, deux monstres de plus dans la room ;
  5. retourne au camp : attend `RpgVitals` du boss à PV pleins et le départ des deux loups.

  Journal de chaque étape ; échec explicite sinon. `SKILL.md` : la commande.
- [ ] **Step 4 : documentation.** `docs/architecture.md` (flux combat : incantation de monstre, télégraphes, phases) ; `AGENTS.md` ; spec : statut « livrée », Décision 4 du plan reportée en §8.1.
- [ ] **Step 5 : porte et preuve.** Porte complète au premier plan (script pas à pas) → tout vert. Sur l'instance alt (`amb-alt.ps1`, plugin web republié, contenu poussé par `driver.mjs content`) : `boss`, `skills`, `combat`, `smoke` → verts. `check-authorization-surface.mjs` → 0 (aucune route nouvelle).
- [ ] **Step 6 : commit** `monstres : contenu de démonstration, preuve boss, documentation`.
- [ ] **Step 7 : contrat client.** Envoyer aux sessions `vortex-rpg-client-bf` et `vortex-rpg-client-f4` : 9025 et 9026 (champs dans l'ordre), `rank` en fin de 9009, `immune`, `RemainingMs = -1`, compétences de monstre par `/api/public/content/definitions/skill` (`classes` vide, `telegraph`), textes des cris ; ce que le client dessine (spec §9.4).
